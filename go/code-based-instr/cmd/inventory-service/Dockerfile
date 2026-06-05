FROM golang:1.24.2-alpine AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -o /out/inventory-service ./cmd/inventory-service

FROM alpine:3.21
RUN apk add --no-cache ca-certificates
COPY --from=build /out/inventory-service /inventory-service
ENTRYPOINT ["/inventory-service"]
