pluginManagement {
    repositories {
        mavenCentral()
        gradlePluginPortal()
    }
}
rootProject.name = "otel-java-sample"
include(":src:order-service", ":src:inventory-service")