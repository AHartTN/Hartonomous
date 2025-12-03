.PHONY: help build test clean restore migrate-create migrate-up migrate-down docker-up docker-down deploy-all

# Default target
help:
	@echo "Hartonomous Database Project - Available Commands"
	@echo "=================================================="
	@echo ""
	@echo "Building:"
	@echo "  make restore          - Restore NuGet packages"
	@echo "  make build            - Build the solution"
	@echo "  make clean            - Clean build artifacts"
	@echo ""
	@echo "Testing:"
	@echo "  make test             - Run all tests"
	@echo "  make test-coverage    - Run tests with coverage"
	@echo ""
	@echo "Database Migrations:"
	@echo "  make migrate-create NAME=MigrationName ENV=localhost"
	@echo "  make migrate-up ENV=localhost"
	@echo "  make migrate-down ENV=localhost"
	@echo ""
	@echo "Docker:"
	@echo "  make docker-up        - Start all Docker containers"
	@echo "  make docker-down      - Stop all Docker containers"
	@echo "  make docker-logs      - View Docker logs"
	@echo ""
	@echo "Deployment:"
	@echo "  make deploy ENV=localhost"
	@echo "  make deploy-all       - Deploy all environments"
	@echo "  make teardown ENV=localhost"
	@echo ""

# Build targets
restore:
	dotnet restore

build: restore
	dotnet build --configuration Release --no-restore

clean:
	dotnet clean
	rm -rf **/bin **/obj

# Test targets
test:
	dotnet test --configuration Release

test-coverage:
	dotnet test --collect:"XPlat Code Coverage" --logger trx

# Migration targets
migrate-create:
	@if [ -z "$(NAME)" ]; then \
		echo "Error: NAME is required. Usage: make migrate-create NAME=MigrationName ENV=localhost"; \
		exit 1; \
	fi
	cd Hartonomous.Db && ASPNETCORE_ENVIRONMENT=$(ENV) dotnet ef migrations add $(NAME)

migrate-up:
	@if [ -z "$(ENV)" ]; then \
		echo "Error: ENV is required. Usage: make migrate-up ENV=localhost"; \
		exit 1; \
	fi
	cd Hartonomous.Db && ASPNETCORE_ENVIRONMENT=$(ENV) dotnet ef database update

migrate-down:
	@if [ -z "$(ENV)" ]; then \
		echo "Error: ENV is required. Usage: make migrate-down ENV=localhost"; \
		exit 1; \
	fi
	cd Hartonomous.Db && ASPNETCORE_ENVIRONMENT=$(ENV) dotnet ef database update 0

# Docker targets
docker-up:
	docker-compose up -d

docker-down:
	docker-compose down

docker-logs:
	docker-compose logs -f

# Deployment targets
deploy:
	@if [ -z "$(ENV)" ]; then \
		echo "Error: ENV is required. Usage: make deploy ENV=localhost"; \
		exit 1; \
	fi
	pwsh ./scripts/deploy-environment.ps1 -Environment $(ENV)

deploy-all:
	pwsh ./scripts/deploy-all.ps1

teardown:
	@if [ -z "$(ENV)" ]; then \
		echo "Error: ENV is required. Usage: make teardown ENV=localhost"; \
		exit 1; \
	fi
	pwsh ./scripts/teardown-environment.ps1 -Environment $(ENV)
