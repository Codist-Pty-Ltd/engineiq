# Dev/CI only — not shipped to clients.
FROM python:3.12-slim-bookworm

WORKDIR /app
COPY python/rule-validator/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY config/standards-templates/clean-architecture.yaml /app/config/standards-templates/clean-architecture.yaml
COPY python/rule-validator/validate.py /app/validate.py
COPY python/rule-validator/fixtures /app/fixtures

ENTRYPOINT ["python", "validate.py"]
CMD ["--config", "/app/config/standards-templates/clean-architecture.yaml", "--fixtures", "/app/fixtures"]
