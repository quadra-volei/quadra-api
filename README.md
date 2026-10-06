# Quadra API

Backend of Quadra, a volleyball app: match organization, live game, player profile and group
ranking. .NET 10, PostgreSQL + PostGIS, one container.

```bash
docker compose up -d postgres
dotnet run --project src/Quadra.Api      # http://localhost:5075
dotnet test
```

Integration tests need Docker running.

- Rules and conventions for working in this repo: [CLAUDE.md](CLAUDE.md)
- What is done, pending and out of the MVP: [docs/SCOPE.md](docs/SCOPE.md)
- Decisions and their reasons: [docs/DECISIONS.md](docs/DECISIONS.md)
- Modules and hosting: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
