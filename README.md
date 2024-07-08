- **Idea**

 In several advanced parking systems, a specified or relatively complex pricing algorithm
is applied to calculate vehicle’s fee to be paid before leaving the parking, the fee covers
the whole period in which such a vehicle was present in the parking.
In such a system, can be present various feeing models per week, each model specifies
rules on some or all time intervals of a week day.
The project aims to realize a distributed payment system that can be used for parking’s
payment machines to calculate vehicle’s fee. The calculation of fee is distributed
into {week, day, time interval} units which got assigned correspondently to their clusters
(each cluster is a set of containerized microservices with load balancing and fault
tolerance techniques in order to offer a high-available system).


![Screenshot 2024-07-08 150530](https://github.com/Abudo-S/PaymentMachinePricingSystem/assets/40835481/7a39a637-8796-4a4d-808b-dadfa2020ae7)

- **Components**
1. Payment machines simulator: which is responsable of requesting the calculation of a
fee (Note that: several payment machine can request even simultaneously the corresponding
for a certain period of time). A Payment machine can also be enabled
to perform CRUD operations on model objects using administrator account/token on
payment machine.(Any request is sent to the middleware in order to receive a further
asynchronous response if needed).

2. Asynchronous persistent middleware: a gRPC service that elaborates requests before
forwarding them to the clusters. (The middleware knows only the coordinator’s endpoint
of each cluster).

3. Clusters of gRPC microservices:
• Week-pay-model cluster: contains a set of week-pay-model nodes for which there’s
a defined coordinator node that is enabled with load-balancing component to
forward requests to an appropriate service. Each service receives the requests
relative to week models like {create, read, update, delete, calculate week fee}.
• Day-pay-model cluster: contains a set of day-pay-model nodes for which there’s
a pre-defined coordinator node that is enabled with load-balancing component
to forward requests to an appropriate service. Each service receives the requests
relative to day models like {create, read, update, delete, calculate day fee}.
• Time-interval cluster: contains a set of time-interval-rule nodes for which there’s
a pre-defined coordinator node that is enabled with load-balancing component
to forward requests to an appropriate service. Each service receives the requests
relative to time-interval rules like {create, read, update, delete, calculate timeinterval
fee with most appropriate rules}.

4. Redis Cache DB per cluster:
• Week requests cache: used to handle temporarily all the received requests of
week-pay-model cluster, so these requests can be used subsequently as a copy to
be passed to a microservice instead of a failed one during another microservice’s
processing.
• Day requests cache: used to handle temporarily all the received requests of daymodel
cluster, so these requests can be used subsequently as a copy to be passed
to a microservice instead of a failed one during another microservice’s processing.
• Time interval requests cache: used to handle temporarily all the received requests
of time-interval cluster, so these requests can be used subsequently as a copy to
be passed to a microservice instead of a failed one during another microservice’s
processing.

5. Mongo DB: on which any cluster’s microservice has access, in order to apply elaborated
request’s CRUD operations. (Each microservice has its own model’s DB service).

- **Examples of requests coming to the middleware**

  - ADD(TimeInterval)
  - SetMentainance(paymentMachine, mode)
  - CalculateFee('2024/03/01 15:00:00', '2024/03/01 15:10:00'): the request is considered as an interval calculation, so it will be forwarded to "TimeIntervalService".
  - CalculateFee('2024/03/01 15:00:00', '2024/03/01 23:00:00'): the request is considered as a day calculation, so it will be forwarded to "DayRateService", then the microservice will elaborate it and split it for intervals.
  - ...
