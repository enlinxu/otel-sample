package com.example.inventoryservice;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.boot.ApplicationRunner;
import org.springframework.context.annotation.Bean;

interface InventoryRepository extends JpaRepository<InventoryItem, Integer> {}

@Entity
class InventoryItem {
    @Id
    private Integer itemId;

    @Column(name = "available")
    private Integer available;

    public InventoryItem() {}

    public InventoryItem(Integer itemId, Integer available) {
        this.itemId = itemId;
        this.available = available;
    }

    public Integer getItemId() { return itemId; }
    public void setItemId(Integer itemId) { this.itemId = itemId; }
    public Integer getAvailable() { return available; }
    public void setAvailable(Integer available) { this.available = available; }
}

class DataInitializer {

    @Bean
    ApplicationRunner initInventory(InventoryRepository repository) {
        return args -> {
            repository.save(new InventoryItem(1, 42));
            repository.save(new InventoryItem(2, 5));
            repository.save(new InventoryItem(3, 0));
        };
    }
}