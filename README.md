- **Idea**

  The idea is to realize a distributed payment system that can be used for parking's payment machines to calculate vehicle's fee.

  In several advanced parking systems, a specified or relatively complex pricing algorithm is applied to calculate vehicle's fee to be paid before leaving the parking, the fee covers the whole period in which such a vehicle was present in the parking.

  In such a system can be present various models per week, each model specifies rules on some or all time intervals of a week day.

  The fee calculation is obtained by applying all registered "weekPayModel" on vehicle's period on condition that such a model has at least one day that its time intervals coincide with vehicle's period.

  Each time interval rule has a priority to be applied in case of multiple models that cover such a period/interval of time; when "TotalCoveredMinutes" of interval is reached, the "timeIntervalRule" isn't considered again during the same fee calculation.

  The input period of vehicle has unlimited range of time; it can be in minutes, hours, days, months or even a mix between all of them, so the algorithm should be robust while categorizing the calculation per each model considering days and time intervals with several rules and priorities.

  Restriction: the attribute "TotalCoveredMinutes" in TimeIntervalRule can't have a value higher than a predefined value (ex. 60min).


- **Components**

  1. Payment machines simulator: several payment machine can request even simultaneously the corresponding for a certain period of time.

  2. Admin client simulator [can be embedded in pt.1- to be decided later]:  an administrator account on payment machine or online platform which can apply CRUD operations on model objects, can also register payment machines and set them in/out maintenance.

  3. Asynchronous persistent middleware: a gRPC interceptor that elaborates requests before forwarding them to the microservices . [Note we may consider also MQTT through payment machines' side and this middleware].

  4. Microservices (gRPC):

     - WeekPayModelService: receives the requests in terms of weeks that should be elaborated and apply the pricing for a specified week, unifies various days' calculated partial fee. 

     - DayPayModelService: applies day's free minutes and distributed day's period on various time intervals, then unifies various intervals' calculated partial fee. 

     - TimeIntervalService: classifies rules' priorities and apply various amounts considering frequency of minutes and total covered minutes.

  5. A cluster of gRPC microservice's instances will run with its own coordinator of the same type:
     - A coordinator instance recieves middleware's requests and using a load balancer "YARP", it'll forward it to an appropriate cluster node.
     - A cluster node will write the received request in shared distributed cache "Redis", informing all cluster nodes of the occured request-assignment with requestID and expiry.
     - Cluster nodes should remain in Ping with the coordinator; so if it fails, they can establish a "Bully" election considering their ids.
     - A node which finishes elaborating its request, it should inform the middleware with the result, then the same node should clean request's data in cache. Also it should inform other nodes for the accomplished request.
     - Another node can handle an expired request (supposing node's failure), informing all other nodes of the new expiry (the oldest information wins).

- **Examples of requests coming to the middleware**

  - ADD(TimeInterval)
  - SetMentainance(paymentMachine, mode)
  - CalculateFee('2024/03/01 15:00:00', '2024/03/01 15:10:00'): the request is considered as an interval calculation, so it will be forwarded to "TimeIntervalService".
  - CalculateFee('2024/03/01 15:00:00', '2024/03/01 23:00:00'): the request is considered as a day calculation, so it will be forwarded to "DayRateService", then the microservice will elaborate it and split it for intervals.
  - ...
