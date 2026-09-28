.PHONY: up down logs scan test
up:
	docker compose up --build

down:
	docker compose down

logs:
	docker compose logs -f

scan:
	bash scripts/scan-for-sensitive.sh

test:
	dotnet test tests/Portfolio.Ltl.Api.Tests.csproj
