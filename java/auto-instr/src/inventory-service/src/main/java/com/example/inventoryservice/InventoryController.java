package com.example.inventoryservice;

import jakarta.persistence.*;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RestController;

import java.util.Map;

@RestController
public class InventoryController {

    private final InventoryRepository inventoryRepository;

    public InventoryController(InventoryRepository inventoryRepository) {
        this.inventoryRepository = inventoryRepository;
    }

    @GetMapping("/health")
    public ResponseEntity<Map<String, String>> health() {
        return ResponseEntity.ok(Map.of("status", "ok", "service", "inventory-service"));
    }

    @GetMapping("/inventory/{id}")
    public ResponseEntity<InventoryResponse> getInventory(@PathVariable Integer id) {
        InventoryItem item = inventoryRepository.findById(id).orElse(null);

        int available = item != null ? item.getAvailable() : 0;

        return ResponseEntity.ok(new InventoryResponse(id, available, 0));
    }

    record InventoryResponse(Integer itemId, Integer available, Integer clickHouseCount) {}
}