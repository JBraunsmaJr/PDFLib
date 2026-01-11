#!/bin/bash

set -e
docker compose -f ./../compose.yaml build pdflib.benchmarks
docker compose -f ./../compose.yaml run --rm pdflib.benchmarks