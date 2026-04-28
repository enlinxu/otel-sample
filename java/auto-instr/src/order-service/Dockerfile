FROM eclipse-temurin:17-jre
WORKDIR /app

ARG OTEL_AGENT_VERSION=2.6.1
ARG OTEL_AGENT_URL=https://github.com/open-telemetry/opentelemetry-java-instrumentation/releases/download/v${OTEL_AGENT_VERSION}/opentelemetry-javaagent-${OTEL_AGENT_VERSION}.jar
ARG OTEL_AGENT_SHA=""

RUN apt-get update && apt-get install -y wget \
    && wget -O /app/opentelemetry-javaagent.jar ${OTEL_AGENT_URL} \
    && if [ -n "${OTEL_AGENT_SHA}" ]; then \
         echo "${OTEL_AGENT_SHA}  /app/opentelemetry-javaagent.jar" | sha256sum -c -; \
       fi \
    && chmod 644 /app/opentelemetry-javaagent.jar \
    && rm -rf /var/lib/apt/lists/*

COPY build/libs/order-service-1.0.0.jar app.jar

ENTRYPOINT ["java", "-javaagent:/app/opentelemetry-javaagent.jar", "-jar", "app.jar"]