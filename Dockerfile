# Rabbid Lab in a container: built with the .NET SDK, run on the ASP.NET runtime image (Debian).
#   docker build -t rabbidlab .
#   docker run -d -p 8420:8420 -v rabbidlab:/data --name rabbidlab rabbidlab
# Everything it keeps is in /data: mount a volume there, or it's lost when the container is replaced.
# It builds for x64 and ARM (a Raspberry Pi): the SDK runs on the machine building it and publishes for the one asked
# for (docker buildx build --platform linux/arm64 .), so nothing is emulated but the last step.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
# DEV=true builds Rabbid Lab Dev (docker-compose.dev.yml): its own name and a DEV tag, beside the release.
ARG DEV=false
WORKDIR /src
# The projects first, so their packages are restored once and kept while only the code changes: Rabbid Lab, and the
# update engine it's built with (Src/Utilities).
COPY Src/RabbidLab.Server/RabbidLab.Server.csproj Src/RabbidLab.Server/
COPY Src/Utilities/RabbidLab.Updater/RabbidLab.Updater.csproj Src/Utilities/RabbidLab.Updater/
RUN dotnet restore Src/RabbidLab.Server/RabbidLab.Server.csproj -a $TARGETARCH
COPY Src/ Src/
RUN dotnet publish Src/RabbidLab.Server/RabbidLab.Server.csproj -c Release -a $TARGETARCH -o /app --no-restore -p:Dev=$DEV -p:DebugType=None

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8420 \
    DataPath=/data
RUN mkdir -p /data && chown "$APP_UID" /data
VOLUME /data
EXPOSE 8420
USER $APP_UID
HEALTHCHECK --interval=1m --timeout=5s --start-period=20s CMD ["dotnet", "RabbidLab.Server.dll", "--health"]
ENTRYPOINT ["dotnet", "RabbidLab.Server.dll"]
