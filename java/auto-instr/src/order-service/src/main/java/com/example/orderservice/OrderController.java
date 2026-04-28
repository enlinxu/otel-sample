package com.example.orderservice;

import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.client.RestTemplate;

@RestController
public class OrderController {

    private final RestTemplate restTemplate;
    private final String inventoryServiceUrl;

    public OrderController(RestTemplate restTemplate) {
        this.restTemplate = restTemplate;
        this.inventoryServiceUrl = System.getenv().getOrDefault(
            "INVENTORY_SERVICE_URL",
            "http://inventory-service:8080"
        );
    }

    @GetMapping("/health")
    public ResponseEntity<HealthResponse> health() {
        return ResponseEntity.ok(new HealthResponse("ok", "order-service"));
    }

    @GetMapping("/order/{id}")
    public ResponseEntity<OrderResponse> getOrder(@PathVariable Integer id) {
        InventoryResponse inventory = restTemplate.getForObject(
            inventoryServiceUrl + "/inventory/" + id,
            InventoryResponse.class
        );

        if (inventory == null) {
            return ResponseEntity.status(502).build();
        }

        return ResponseEntity.ok(new OrderResponse(id, "processed", inventory));
    }

    record HealthResponse(String status, String service) {}
    record InventoryResponse(Integer itemId, Integer available) {}
    record OrderResponse(Integer orderId, String status, InventoryResponse inventory) {}
}