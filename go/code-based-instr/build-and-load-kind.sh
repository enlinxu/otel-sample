#!/usr/bin/env bash
set -euo pipefail

KIND_CLUSTER_NAME="${KIND_CLUSTER_NAME:-kind}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "Building order-service image..."
docker build -t otel-sample-go/order-service:latest -f "${SCRIPT_DIR}/cmd/order-service/Dockerfile" "${SCRIPT_DIR}"

echo "Building inventory-service image..."
docker build -t otel-sample-go/inventory-service:latest -f "${SCRIPT_DIR}/cmd/inventory-service/Dockerfile" "${SCRIPT_DIR}"

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
load_image otel-sample-go/order-service:latest
load_image otel-sample-go/inventory-service:latest

echo "Done."
