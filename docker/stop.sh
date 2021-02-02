#/bin/bash
#docker run -d -v /dgraph:/dgraph --net=host dgraph/standalone
INSTANCE_FILE=/tmp/dgraphcontainer
if [[ -f "$INSTANCE_FILE" ]]
then 
	CONTAINER_ID=$(cat "$INSTANCE_FILE")
	echo 'Stopping Dgraph container with ID '$CONTAINER_ID
	docker stop $CONTAINER_ID
	rm "$INSTANCE_FILE"
	exit 0
fi
echo 'No running dgraph containers were found'
