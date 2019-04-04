# Overview
This strategy is optimized for limited max connection count such as web application in docker container.

Sometimes, we estimate max container count by below fomula.

`(max container count) = (database max connections) / (connections per container)`

In this case, max connection count is determined by environment, and connection strategy is desired to use limited connections efficiently.

# Details
- Use global connection pool that contains all allowed connections
- All sql command executions rent connection from pool
- If no connection can use, queue command execution