#!/usr/bin/env bash
set -euo pipefail

KIND_CLUSTER_NAME="${KIND_CLUSTER_NAME:-kind}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "${SCRIPT_DIR}")"

build_and_tag() {
    local service_name="$1"
    local service_dir="$2"

    echo "Building order-service image..."
    docker build -t "otel-java/order-service:latest" "${service_dir}"

    echo "Building inventory-service image..."
    docker build -t "otel-java/inventory-service:latest" "${service_dir}"
}

load_image() {
    local image="$1"

    if kind load docker-image "${image}" --name "${KIND_CLUSTER_NAME}"; then
        return 0
    fi

    echo "kind load failed for ${image}; using direct containerd import fallback..."
    local nodes
    nodes="$(kind get nodes --name "${KIND_CLUSTER_NAME}")"
    while IFS= read -r node; do
        [ -z "${node}" ] && continue
        echo "Importing ${image} into ${node}..."
        docker save "${image}" | docker exec -i "${node}" ctr -n k8s.io images import -
    done <<< "${nodes}"
}

cd "${ROOT_DIR}"
./gradlew build --no-daemon

cd src/order-service && docker build -t otel-java/order-service:latest . && cd -
load_image otel-java/order-service:latest

cd src/inventory-service && docker build -t otel-java/inventory-service:latest . && cd -
load_image otel-java/inventory-service:latest

echo "Done."