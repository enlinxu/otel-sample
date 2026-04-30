#!/usr/bin/env bash
set -euo pipefail

KIND_CLUSTER_NAME="${KIND_CLUSTER_NAME:-kind}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

ORDER_DIR="${SCRIPT_DIR}/src/order-service"
INVENTORY_DIR="${SCRIPT_DIR}/src/inventory-service"

echo "Building order-service image..."
docker build -t otel-sample-zero-masstransit/order-service:latest "${ORDER_DIR}"

echo "Building inventory-service image..."
docker build -t otel-sample-zero-masstransit/inventory-service:latest "${INVENTORY_DIR}"

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

echo "Loading images into kind cluster ${KIND_CLUSTER_NAME}..."
load_image otel-sample-zero-masstransit/order-service:latest
load_image otel-sample-zero-masstransit/inventory-service:latest

echo "Done."
