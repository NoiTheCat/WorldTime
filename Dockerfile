FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY * ./

ARG PUBLISH_PROFILE=Release

# Restore and publish
RUN dotnet publish -c ${PUBLISH_PROFILE} -o /build

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final

WORKDIR /app
COPY --from=build /build .

# to do: healthcheck
USER nobody
ENTRYPOINT ["/app/WorldTime"]

