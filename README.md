# pulumigraph
Simple console app that takes the exported data from pulumi and uploads it to neo4j. The code is just something that was thrown together one evening, so not state of the art... but it is working :)

## Install tool

This is distributed as a dotnet tool so install with

    > dotnet tool install -g pulumigraph

## Run tool

To get a neo4j instance up and running you can run:

    > docker run --publish=7474:7474 --publish=7687:7687 --name neo4j --volume=$HOME/neo4j/data:/data -v $HOME/neo4j/import:/var/lib/neo4j/import -e NEO4J_AUTH=none -e NEO4JLABS_PLUGINS=\[\"apoc\"\] neo4j

You have to enable the `apoc` plugin as done above to be able to import the data. If you run docker on Windows you might have to start the container from WSL for some reason to get the plugin to work.

Verify that neo4j is running by going to http://localhost:7474 for the web UI. It is also important that th `7687` ports are published, since that is the port the application talks to neo4j on.

To run the tool, given that you have logged in with `pulumi login`, you just run

    > pulumigraph

This will load all the stacks you have access to, created nodes and edges and uplod them to neo4j. It will also print a lot of information. When finished you can go back to http://localhost:7474 and start query the data. To get all data just run:

    MATCH (n) RETURN n

If you need to authenticated or use another url than the default bolt://localhost:7687 you have the following options:

    OPTIONS:

        --connection-string, -c <string>
                            Optional connection string to neo4j, if not provided bolt://localhost:7687 will be used, take precedence over host and port.
        --host, -h <string>   Optional host name, will only be used if provided and connection isn't specified
        --port, -p <string>   Optional port, will only be used if provided and connection isn't specified, will default to 7687 if not provided
        --password, -pass <string>
                            Optional password, required if user is provided
        --user, -u <string>   Optional user, if not provided anonymous authentication will be used (the server must be configured to allow that)
        --help                display this list of options.

(Haven't bother to make it super pretty around error handling when parsing arguments, but the functionality is there)

## The future

This tool is not meant to evolve a ton, but might add some minor features like:

* filter on stacks
* connect to neo4j using a connection string
* pulumi token as input
* (feel free to suggest improvements)
