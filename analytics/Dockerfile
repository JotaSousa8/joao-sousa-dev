FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config .
COPY AnalyticsApi.csproj .
RUN dotnet restore
ARG BUILD_SHA=dev
COPY . .
RUN echo "Building ${BUILD_SHA}" && dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ARG BUILD_SHA=dev
ENV ASPNETCORE_URLS=http://+:8080
ENV BUILD_SHA=${BUILD_SHA}
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "AnalyticsApi.dll"]
