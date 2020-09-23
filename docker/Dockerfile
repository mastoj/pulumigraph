FROM pulumi/pulumi-dotnet:2.10.2
# FROM mcr.microsoft.com/dotnet/core/sdk:3.1
WORKDIR /root
COPY wait-for-it.sh .
RUN chmod +x ./wait-for-it.sh
RUN dotnet tool install --global pulumigraph
ENTRYPOINT [ "/root/.dotnet/tools/pulumigraph" ]
