FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src/orchestrator

COPY shared/quantflow.proto /src/shared/
COPY orchestrator/orchestrator.csproj ./
RUN dotnet restore

COPY orchestrator/ ./
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080;http://+:8081
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV Kestrel__Endpoints__Http__Url=http://+:8080
ENV Kestrel__Endpoints__Grpc__Url=http://+:8081
ENV Kestrel__Endpoints__Grpc__Protocols=Http2

EXPOSE 8080
EXPOSE 8081

ENTRYPOINT ["dotnet", "orchestrator.dll"]
