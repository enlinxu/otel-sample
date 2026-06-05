package main

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"net/url"
	"os"
	"strconv"
	"strings"
	"time"

	"github.com/enlinxu/otel-sample/go/code-based-instr/internal/telemetry"
	otelsamplev1 "github.com/enlinxu/otel-sample/go/code-based-instr/proto"
	"github.com/jackc/pgx/v5/pgxpool"
	amqp "github.com/rabbitmq/amqp091-go"
	"github.com/redis/go-redis/v9"
	"go.opentelemetry.io/contrib/instrumentation/google.golang.org/grpc/otelgrpc"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/propagation"
	"go.opentelemetry.io/otel/trace"
	"google.golang.org/grpc"
	"google.golang.org/grpc/reflection"
)

type app struct {
	otelsamplev1.UnimplementedInventoryServiceServer
	tracer             trace.Tracer
	postgres           *pgxpool.Pool
	redis              *redis.Client
	rabbitChannel      *amqp.Channel
	rabbitQueueName    string
	clickHouseURL      string
	clickHouseUsername string
	clickHousePassword string
	clickHouseClient   *http.Client
}

type inventoryEvent struct {
	ItemID     int       `json:"itemId"`
	Available  int       `json:"available"`
	ObservedAt time.Time `json:"observedAt"`
}

func main() {
	ctx := context.Background()
	tp, err := telemetry.NewTracerProvider(ctx, "inventory-service")
	if err != nil {
		log.Fatalf("configure telemetry: %v", err)
	}
	defer shutdown(tp)

	postgresDSN := getenv("POSTGRES_DSN", "postgres://otel:otel@postgres.otel-sample-go.svc.cluster.local:5432/otel")
	redisAddr := getenv("REDIS_ADDR", "redis.otel-sample-go.svc.cluster.local:6379")
	clickHouseURL := getenv("CLICKHOUSE_URL", "http://clickhouse.otel-sample-go.svc.cluster.local:8123")
	clickHouseUser := getenv("CLICKHOUSE_USER", "otel")
	clickHousePassword := getenv("CLICKHOUSE_PASSWORD", "otel")
	rabbitMQURL := getenv("RABBITMQ_URL", "amqp://guest:guest@rabbitmq.otel-sample-go.svc.cluster.local:5672/")
	rabbitMQQueue := getenv("RABBITMQ_QUEUE", "inventory-events")

	postgresPool, err := pgxpool.New(ctx, postgresDSN)
	if err != nil {
		log.Fatalf("postgres pool: %v", err)
	}
	defer postgresPool.Close()

	redisClient := redis.NewClient(&redis.Options{Addr: redisAddr})
	defer redisClient.Close()

	rabbitConn, err := dialRabbitMQ(ctx, rabbitMQURL)
	if err != nil {
		log.Fatalf("rabbitmq dial: %v", err)
	}
	defer rabbitConn.Close()

	rabbitChannel, err := rabbitConn.Channel()
	if err != nil {
		log.Fatalf("rabbitmq channel: %v", err)
	}
	defer rabbitChannel.Close()

	if _, err := rabbitChannel.QueueDeclare(rabbitMQQueue, true, false, false, false, nil); err != nil {
		log.Fatalf("rabbitmq queue declare: %v", err)
	}

	service := &app{
		tracer:             otel.Tracer("github.com/enlinxu/otel-sample/go/code-based-instr/inventory-service"),
		postgres:           postgresPool,
		redis:              redisClient,
		rabbitChannel:      rabbitChannel,
		rabbitQueueName:    rabbitMQQueue,
		clickHouseURL:      clickHouseURL,
		clickHouseUsername: clickHouseUser,
		clickHousePassword: clickHousePassword,
		clickHouseClient:   &http.Client{Timeout: 10 * time.Second},
	}

	if err := service.initDependencies(ctx); err != nil {
		log.Fatalf("init dependencies: %v", err)
	}

	grpcServer := grpc.NewServer(grpc.StatsHandler(otelgrpc.NewServerHandler()))
	otelsamplev1.RegisterInventoryServiceServer(grpcServer, service)
	reflection.Register(grpcServer)

	listener, err := net.Listen("tcp", ":9090")
	if err != nil {
		log.Fatalf("listen: %v", err)
	}

	log.Printf("inventory-service listening on %s", listener.Addr())
	if err := grpcServer.Serve(listener); err != nil {
		log.Fatalf("serve grpc: %v", err)
	}
}

func (a *app) GetInventory(ctx context.Context, req *otelsamplev1.GetInventoryRequest) (*otelsamplev1.GetInventoryResponse, error) {
	itemID := int(req.GetItemId())
	if itemID <= 0 {
		return nil, fmt.Errorf("item_id must be greater than zero")
	}

	available, err := a.queryInventory(ctx, itemID)
	if err != nil {
		recordServerError(ctx, err)
		return nil, err
	}

	if err := a.writeClickHouse(ctx, itemID, available); err != nil {
		recordServerError(ctx, err)
		return nil, err
	}

	count, err := a.countClickHouse(ctx, itemID)
	if err != nil {
		recordServerError(ctx, err)
		return nil, err
	}

	cachedValue, err := a.cacheInventory(ctx, itemID, available)
	if err != nil {
		recordServerError(ctx, err)
		return nil, err
	}

	deliveryMessage, err := a.publishAndConsume(ctx, inventoryEvent{ItemID: itemID, Available: available, ObservedAt: time.Now().UTC()})
	if err != nil {
		recordServerError(ctx, err)
		return nil, err
	}

	return &otelsamplev1.GetInventoryResponse{
		ItemId:                  int32(itemID),
		Available:               int32(available),
		PostgresSource:          "inventory",
		ClickhouseRequestCount:  int32(count),
		RedisCacheValue:         cachedValue,
		RabbitmqDeliveryMessage: deliveryMessage,
	}, nil
}

func (a *app) initDependencies(ctx context.Context) error {
	if err := retry(ctx, 30, 2*time.Second, func(ctx context.Context) error {
		_, err := a.postgres.Exec(ctx, `
			CREATE TABLE IF NOT EXISTS inventory (
				item_id INTEGER PRIMARY KEY,
				available INTEGER NOT NULL
			);
			INSERT INTO inventory (item_id, available) VALUES (1, 42)
			ON CONFLICT (item_id) DO UPDATE SET available = EXCLUDED.available;
		`)
		return err
	}); err != nil {
		return fmt.Errorf("init postgres: %w", err)
	}

	if err := retry(ctx, 30, 2*time.Second, func(ctx context.Context) error {
		return a.execClickHouse(ctx, "CREATE TABLE IF NOT EXISTS inventory_requests (item_id Int32, available Int32, observed_at DateTime) ENGINE = MergeTree ORDER BY (item_id, observed_at)")
	}); err != nil {
		return fmt.Errorf("init clickhouse: %w", err)
	}

	if err := retry(ctx, 30, 2*time.Second, func(ctx context.Context) error {
		return a.redis.Ping(ctx).Err()
	}); err != nil {
		return fmt.Errorf("init redis: %w", err)
	}

	return nil
}

func (a *app) queryInventory(ctx context.Context, itemID int) (int, error) {
	ctx, span := a.tracer.Start(ctx, "db.postgresql.query inventory", trace.WithSpanKind(trace.SpanKindClient), trace.WithAttributes(
		attribute.String("db.system", "postgresql"),
		attribute.String("db.name", "otel"),
		attribute.String("db.operation.name", "SELECT"),
		attribute.String("db.collection.name", "inventory"),
		attribute.String("db.query.text", "SELECT available FROM inventory WHERE item_id = $1"),
	))
	defer span.End()

	var available int
	if err := a.postgres.QueryRow(ctx, "SELECT available FROM inventory WHERE item_id = $1", itemID).Scan(&available); err != nil {
		span.RecordError(err)
		span.SetStatus(codes.Error, err.Error())
		return 0, err
	}
	return available, nil
}

func (a *app) writeClickHouse(ctx context.Context, itemID, available int) error {
	ctx, span := a.tracer.Start(ctx, "db.clickhouse.insert inventory_requests", trace.WithSpanKind(trace.SpanKindClient), trace.WithAttributes(
		attribute.String("db.system", "clickhouse"),
		attribute.String("db.name", "default"),
		attribute.String("db.operation.name", "INSERT"),
		attribute.String("db.collection.name", "inventory_requests"),
	))
	defer span.End()

	statement := fmt.Sprintf("INSERT INTO inventory_requests (item_id, available, observed_at) VALUES (%d, %d, now())", itemID, available)
	span.SetAttributes(attribute.String("db.query.text", statement))
	if err := a.execClickHouse(ctx, statement); err != nil {
		span.RecordError(err)
		span.SetStatus(codes.Error, err.Error())
		return err
	}
	return nil
}

func (a *app) countClickHouse(ctx context.Context, itemID int) (int, error) {
	ctx, span := a.tracer.Start(ctx, "db.clickhouse.select inventory_requests_count", trace.WithSpanKind(trace.SpanKindClient), trace.WithAttributes(
		attribute.String("db.system", "clickhouse"),
		attribute.String("db.name", "default"),
		attribute.String("db.operation.name", "SELECT"),
		attribute.String("db.collection.name", "inventory_requests"),
	))
	defer span.End()

	statement := fmt.Sprintf("SELECT count() FROM inventory_requests WHERE item_id = %d FORMAT TabSeparated", itemID)
	span.SetAttributes(attribute.String("db.query.text", statement))
	result, err := a.queryClickHouse(ctx, statement)
	if err != nil {
		span.RecordError(err)
		span.SetStatus(codes.Error, err.Error())
		return 0, err
	}

	count, err := strconv.Atoi(strings.TrimSpace(result))
	if err != nil {
		span.RecordError(err)
		span.SetStatus(codes.Error, err.Error())
		return 0, err
	}
	return count, nil
}

func (a *app) cacheInventory(ctx context.Context, itemID, available int) (string, error) {
	cacheKey := fmt.Sprintf("inventory-events:%d", itemID)
	cacheValue := strconv.Itoa(available)

	setCtx, setSpan := a.tracer.Start(ctx, "db.redis.lpush inventory", trace.WithSpanKind(trace.SpanKindClient), trace.WithAttributes(
		attribute.String("db.system", "redis"),
		attribute.String("db.operation.name", "LPUSH"),
		attribute.Int("db.redis.database_index", 0),
		attribute.String("db.query.text", fmt.Sprintf("LPUSH %s %s", cacheKey, cacheValue)),
	))
	if err := a.redis.LPush(setCtx, cacheKey, cacheValue).Err(); err != nil {
		setSpan.RecordError(err)
		setSpan.SetStatus(codes.Error, err.Error())
		setSpan.End()
		return "", err
	}
	setSpan.End()

	getCtx, getSpan := a.tracer.Start(ctx, "db.redis.rpop inventory", trace.WithSpanKind(trace.SpanKindClient), trace.WithAttributes(
		attribute.String("db.system", "redis"),
		attribute.String("db.operation.name", "RPOP"),
		attribute.Int("db.redis.database_index", 0),
		attribute.String("db.query.text", fmt.Sprintf("RPOP %s", cacheKey)),
	))
	defer getSpan.End()

	value, err := a.redis.RPop(getCtx, cacheKey).Result()
	if err != nil {
		getSpan.RecordError(err)
		getSpan.SetStatus(codes.Error, err.Error())
		return "", err
	}
	return value, nil
}

func (a *app) publishAndConsume(ctx context.Context, event inventoryEvent) (string, error) {
	body, err := json.Marshal(event)
	if err != nil {
		return "", err
	}

	headers := amqp.Table{}
	carrier := amqpHeaderCarrier(headers)
	otel.GetTextMapPropagator().Inject(ctx, carrier)

	publishCtx, publishSpan := a.tracer.Start(ctx, "messaging.rabbitmq.publish inventory-events", trace.WithSpanKind(trace.SpanKindProducer), trace.WithAttributes(
		attribute.String("messaging.system", "rabbitmq"),
		attribute.String("messaging.destination.name", a.rabbitQueueName),
		attribute.String("messaging.operation.type", "send"),
		attribute.String("messaging.operation.name", "publish"),
		attribute.String("network.protocol.name", "amqp"),
		attribute.String("network.protocol.version", "0.9.1"),
	))
	if err := a.rabbitChannel.PublishWithContext(publishCtx, "", a.rabbitQueueName, false, false, amqp.Publishing{
		ContentType: "application/json",
		Body:        body,
		Headers:     headers,
	}); err != nil {
		publishSpan.RecordError(err)
		publishSpan.SetStatus(codes.Error, err.Error())
		publishSpan.End()
		return "", err
	}
	publishSpan.End()

	delivery, ok, err := a.rabbitChannel.Get(a.rabbitQueueName, true)
	if err != nil {
		return "", err
	}
	if !ok {
		return "", fmt.Errorf("no rabbitmq message available")
	}

	consumeCarrier := amqpHeaderCarrier(delivery.Headers)
	consumeCtx := otel.GetTextMapPropagator().Extract(ctx, consumeCarrier)
	_, consumeSpan := a.tracer.Start(consumeCtx, "messaging.rabbitmq.process inventory-events", trace.WithSpanKind(trace.SpanKindConsumer), trace.WithAttributes(
		attribute.String("messaging.system", "rabbitmq"),
		attribute.String("messaging.destination.name", a.rabbitQueueName),
		attribute.String("messaging.operation.type", "process"),
		attribute.String("messaging.operation.name", "deliver"),
		attribute.String("network.protocol.name", "amqp"),
		attribute.String("network.protocol.version", "0.9.1"),
	))
	consumeSpan.End()

	return string(delivery.Body), nil
}

func (a *app) execClickHouse(ctx context.Context, statement string) error {
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, a.clickHouseURL, strings.NewReader(statement))
	if err != nil {
		return err
	}
	if a.clickHouseUsername != "" {
		req.SetBasicAuth(a.clickHouseUsername, a.clickHousePassword)
	}
	resp, err := a.clickHouseClient.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode >= 300 {
		body, _ := io.ReadAll(io.LimitReader(resp.Body, 4096))
		return fmt.Errorf("clickhouse status %d: %s", resp.StatusCode, strings.TrimSpace(string(body)))
	}
	_, _ = io.Copy(io.Discard, resp.Body)
	return nil
}

func (a *app) queryClickHouse(ctx context.Context, statement string) (string, error) {
	requestURL := a.clickHouseURL + "?query=" + url.QueryEscape(statement)
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, requestURL, bytes.NewBuffer(nil))
	if err != nil {
		return "", err
	}
	if a.clickHouseUsername != "" {
		req.SetBasicAuth(a.clickHouseUsername, a.clickHousePassword)
	}
	resp, err := a.clickHouseClient.Do(req)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()
	if resp.StatusCode >= 300 {
		body, _ := io.ReadAll(io.LimitReader(resp.Body, 4096))
		return "", fmt.Errorf("clickhouse status %d: %s", resp.StatusCode, strings.TrimSpace(string(body)))
	}
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return "", err
	}
	return string(body), nil
}

func dialRabbitMQ(ctx context.Context, rabbitMQURL string) (*amqp.Connection, error) {
	var conn *amqp.Connection
	var err error
	for attempt := 0; attempt < 30; attempt++ {
		conn, err = amqp.Dial(rabbitMQURL)
		if err == nil {
			return conn, nil
		}
		select {
		case <-ctx.Done():
			return nil, ctx.Err()
		case <-time.After(2 * time.Second):
		}
	}
	return nil, err
}

func retry(ctx context.Context, attempts int, delay time.Duration, fn func(context.Context) error) error {
	var err error
	for attempt := 0; attempt < attempts; attempt++ {
		err = fn(ctx)
		if err == nil {
			return nil
		}
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-time.After(delay):
		}
	}
	return err
}

func recordServerError(ctx context.Context, err error) {
	span := trace.SpanFromContext(ctx)
	span.RecordError(err)
	span.SetStatus(codes.Error, err.Error())
}

func shutdown(tp interface{ Shutdown(context.Context) error }) {
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	if err := tp.Shutdown(ctx); err != nil {
		log.Printf("shutdown tracer provider: %v", err)
	}
}

func getenv(key, fallback string) string {
	if value := strings.TrimSpace(os.Getenv(key)); value != "" {
		return value
	}
	return fallback
}

type amqpHeaderCarrier amqp.Table

func (c amqpHeaderCarrier) Get(key string) string {
	if value, ok := c[key]; ok {
		return fmt.Sprint(value)
	}
	return ""
}

func (c amqpHeaderCarrier) Set(key, value string) {
	c[key] = value
}

func (c amqpHeaderCarrier) Keys() []string {
	keys := make([]string, 0, len(c))
	for key := range c {
		keys = append(keys, key)
	}
	return keys
}

var _ propagation.TextMapCarrier = amqpHeaderCarrier{}
