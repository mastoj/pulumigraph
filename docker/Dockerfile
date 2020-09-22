FROM mcr.microsoft.com/dotnet/core/sdk:3.1
WORKDIR /root
COPY wait-for-it.sh .
RUN chmod +x ./wait-for-it.sh
RUN dotnet tool install --global pulumigraph --version 0.1.0
ENTRYPOINT [ "/root/.dotnet/tools/pulumigraph" ]
