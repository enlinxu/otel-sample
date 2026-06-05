package main

import (
	"context"
	"log"
	"net"
	"os"
	"strings"
	"time"

	"github.com/enlinxu/otel-sample/go/code-based-instr/internal/telemetry"
	otelsamplev1 "github.com/enlinxu/otel-sample/go/code-based-instr/proto"
	"go.opentelemetry.io/contrib/instrumentation/google.golang.org/grpc/otelgrpc"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials/insecure"
	"google.golang.org/grpc/reflection"
)

type server struct {
	otelsamplev1.UnimplementedOrderServiceServer
	inventoryClient otelsamplev1.InventoryServiceClient
}

func main() {
	ctx := context.Background()
	tp, err := telemetry.NewTracerProvider(ctx, "order-service")
	if err != nil {
		log.Fatalf("configure telemetry: %v", err)
	}
	defer shutdown(tp)

	inventoryAddr := getenv("INVENTORY_SERVICE_ADDR", "inventory-service.otel-sample-go.svc.cluster.local:9090")
	inventoryConn, err := grpc.NewClient(
		inventoryAddr,
		grpc.WithTransportCredentials(insecure.NewCredentials()),
		grpc.WithStatsHandler(otelgrpc.NewClientHandler()),
	)
	if err != nil {
		log.Fatalf("dial inventory-service: %v", err)
	}
	defer inventoryConn.Close()

	grpcServer := grpc.NewServer(grpc.StatsHandler(otelgrpc.NewServerHandler()))
	otelsamplev1.RegisterOrderServiceServer(grpcServer, &server{
		inventoryClient: otelsamplev1.NewInventoryServiceClient(inventoryConn),
	})
	reflection.Register(grpcServer)

	listener, err := net.Listen("tcp", ":9090")
	if err != nil {
		log.Fatalf("listen: %v", err)
	}

	log.Printf("order-service listening on %s", listener.Addr())
	if err := grpcServer.Serve(listener); err != nil {
		log.Fatalf("serve grpc: %v", err)
	}
}

func (s *server) GetOrder(ctx context.Context, req *otelsamplev1.GetOrderRequest) (*otelsamplev1.GetOrderResponse, error) {
	inventory, err := s.inventoryClient.GetInventory(ctx, &otelsamplev1.GetInventoryRequest{ItemId: req.GetItemId()})
	if err != nil {
		return nil, err
	}

	return &otelsamplev1.GetOrderResponse{
		OrderId:   req.GetItemId(),
		Status:    "processed",
		Inventory: inventory,
	}, nil
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
