#/bin/bash
#docker run -d -v /dgraph:/dgraph --net=host dgraph/standalone
INSTANCE_FILE=/tmp/dgraphcontainer
if [[ -f "$INSTANCE_FILE" ]] ; then echo 'Dgraph container already running with ID '"$INSTANCE_FILE" ; exit 0 ; fi
CONTAINER_ID=$(docker run --rm -d -p 127.0.0.1:8080:8080 -p 127.0.0.1:9080:9080 -p 127.0.0.1:8000:8000 -v /dgraph:/dgraph dgraph/standalone:v20.11.3)
echo "Dgraph Container ID: $CONTAINER_ID"
echo "$CONTAINER_ID" > $INSTANCE_FILE


