# pulumigraph
Simple console app that takes the exported data from pulumi and uploads it to neo4j. The code is just something that was thrown together one evening, so not state of the art... but it is working :)

## Install tool

This is distributed as a dotnet tool so install with

    > dotnet tool install -g pulumigraph

## Run tool

As of now it only supports neo4j running locally with no credentials. To start neo4j in a container run:

    > docker run --publish=7474:7474 --publish=7687:7687 --name neo4j --volume=$HOME/neo4j/data:/data -v $HOME/neo4j/import:/var/lib/neo4j/import -e NEO4J_AUTH=none -e NEO4JLABS_PLUGINS=\[\"apoc\"\] neo4j

You have to enable the `apoc` plugin as done above to be able to import the data. If you run docker on Windows you might have to start the container from WSL for some reason to get the plugin to work.

Verify that neo4j is running by going to http://localhost:7474 for the web UI. It is also important that th `7687` ports are published, since that is the port the application talks to neo4j on.

To run the tool, given that you have logged in with `pulumi login`, you just run

    > pulumigraph

This will load all the stacks you have access to, created nodes and edges and uplod them to neo4j. It will also print a lot of information. When finished you can go back to http://localhost:7474 and start query the data. To get all data just run:

    MATCH (n) RETURN n

## The future

This tool is not meant to evolve a ton, but might add some minor features like:

* filter on stacks
* connect to neo4j using a connection string
* pulumi token as input
* (feel free to suggest improvements)
