FROM golang:1.24.2-alpine AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -o /out/order-service ./cmd/order-service

FROM alpine:3.21
RUN apk add --no-cache ca-certificates
COPY --from=build /out/order-service /order-service
ENTRYPOINT ["/order-service"]
