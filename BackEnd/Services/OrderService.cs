using System.Text;
using System.Text.Json;
using HahnCargoSim.Helper;
using HahnCargoSim.Model;
using HahnCargoSim.Model.DTO;
using HahnCargoSim.Services.Interfaces;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HahnCargoSim.Services
{
    public class OrderService : IOrderService
    {
        private readonly SimConfig? config;
        private readonly IGridService gridService;
        private readonly ILoggerService loggerService;
        private readonly CargoTransporterService cargoTransporterService;

        private Dictionary<int, List<FileOrder>> ordersPerTick;
        private readonly Dictionary<string, List<Order>> acceptedOrders;
        private readonly List<Order> availableOrders;
        private int currentOrderId;

        public OrderService(IOptions<SimConfig> config, IGridService gridService, ILoggerService loggerService)
        {
            this.loggerService = loggerService;
            this.config = config.Value;
            if (this.config == null)
            {
                loggerService.Log("Error - Config is null");
                throw new Exception("Config is null");
            }
            this.gridService = gridService;
            this.acceptedOrders = new Dictionary<string, List<Order>>();
            this.availableOrders = new List<Order>();
            this.currentOrderId = 0;
            if (!this.config.UseRandomOrders)
            {
                LoadOrdersFromJson();
            }
            StartOrderReceiver();
        }

        private void StartOrderReceiver()
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };
            var connection = factory.CreateConnection();
            var channel = connection.CreateModel();
            channel.QueueDeclare(queue: "HahnCargoSim_NewOrders", durable: false, exclusive: false, autoDelete: false, arguments: null);
            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                HandleMessage(message);
            };
            channel.BasicConsume(queue: "HahnCargoSim_NewOrders", autoAck: true, consumer: consumer);
        }

        private void HandleMessage(string content)
        {
            var order = JsonSerializer.Deserialize<Order>(content);
            if (order != null && ShouldAcceptOrder(order))
            {
                Accept(order.Id, "system");
                loggerService.Log($"Order {order.Id} accepted.");
            }
        }

        private bool ShouldAcceptOrder(Order order)
        {
            // Get all available transporters
            var availableTransporters = cargoTransporterService.GetAll("system").Where(t => !t.InTransit).ToList();
            if (!availableTransporters.Any())
            {
                loggerService.Log("No available transporters to accept the order.");
                return false;
            }

            // Estimate the cost of moving from the origin to the target node
            var estimatedCost = EstimateTravelCost(order.OriginNode.Id, order.TargetNode.Id);

            // Calculate the potential profit
            var potentialProfit = order.Value - estimatedCost;

            // Ensure the potential profit is positive
            if (potentialProfit <= 0)
            {
                loggerService.Log($"Order {order.Id} is not profitable. Estimated Cost: {estimatedCost}, Order Value: {order.Value}");
                return false;
            }

            // Ensure the delivery date is achievable
            var deliveryTime = EstimateTravelTime(order.OriginNode.Id, order.TargetNode.Id);
            if (DateTime.UtcNow + deliveryTime > order.ExpirationDate)
            {
                loggerService.Log($"Order {order.Id} cannot be delivered on time. Estimated Delivery Time: {deliveryTime}");
                return false;
            }

            loggerService.Log($"Order {order.Id} accepted. Estimated Profit: {potentialProfit}");
            return true;
        }


        private int EstimateTravelCost(int originNodeId, int targetNodeId)
        {
            var totalCost = 0;
            var currentNodeId = originNodeId;

            while (currentNodeId != targetNodeId)
            {
                var connectionId = gridService.ConnectionAvailable(currentNodeId, targetNodeId);
                if (connectionId == -1)
                {
                    // Handle case where no direct connection exists
                    break;
                }
                totalCost += gridService.GetConnectionCost((int)connectionId);
                currentNodeId = targetNodeId; // In a real scenario, you'd continue to find the path
            }

            return totalCost;
        }

        private TimeSpan EstimateTravelTime(int originNodeId, int targetNodeId)
        {
            var totalTime = TimeSpan.Zero;
            var currentNodeId = originNodeId;

            while (currentNodeId != targetNodeId)
            {
                var connectionId = gridService.ConnectionAvailable(currentNodeId, targetNodeId);
                if (connectionId == -1)
                {
                    // Handle case where no direct connection exists
                    break;
                }
                totalTime += gridService.GetConnectionTime((int)connectionId);
                currentNodeId = targetNodeId; // In a real scenario, you'd continue to find the path
            }

            return totalTime;
        }



        public List<Order> GetAllAvailable()
        {
            return availableOrders;
        }

        public List<Order> GetForOwner(string owner)
        {
            return acceptedOrders.ContainsKey(owner) ? acceptedOrders[owner] : new List<Order>();
        }

        public Order? GetAvailable(int orderId)
        {
            return availableOrders.FirstOrDefault(order => order.Id == orderId);
        }

        public Order? GetAccepted(int orderId, string owner)
        {
            return !acceptedOrders.ContainsKey(owner) ? null : acceptedOrders[owner].FirstOrDefault(order => order.Id == orderId);
        }

        public bool Accept(int orderId, string owner)
        {
            var order = availableOrders.FirstOrDefault(o => o.Id == orderId);
            if (order == null) return false;

            availableOrders.Remove(order);

            if (!acceptedOrders.ContainsKey(owner))
            {
                acceptedOrders.Add(owner, new List<Order>());
            }

            acceptedOrders[owner].Add(order);

            loggerService.Log($"Order Accepted: {owner} took order {orderId}");

            return true;
        }

        public bool Finish(int orderId, string owner)
        {
            var order = acceptedOrders[owner].FirstOrDefault(o => o.Id == orderId);
            if (order == null) return false;

            acceptedOrders[owner].Remove(order);

            loggerService.Log($"Order Finished: {owner} finished order {orderId}");
            return true;
        }

        public void RemoveExpiredAvailable()
        {
            availableOrders.RemoveAll(order => order.ExpirationDate <= DateTime.UtcNow);
        }

        public void RemoveAccepted(int orderId, string owner)
        {
            var order = acceptedOrders[owner].FirstOrDefault(o => o.Id == orderId);
            if (order != null)
            {
                acceptedOrders[owner].Remove(order);
            }
        }

        public void Create()
        {
            var rand = new Random();
            var orderAmount = rand.Next(gridService.GetGridSizeFactor()) + 1;

            for (var i = 0; i < orderAmount; i++)
            {
                currentOrderId++;
                var order = OrderGenerator.Generate(gridService, currentOrderId);

                availableOrders.Add(order);
                SendNewOrderMessage(order);

                loggerService.Log($"Created new Order {order.Id} - From {order.OriginNode.Id} to {order.TargetNode.Id}");
            }
        }

        public void Prefabricated(int tick)
        {
            if (!ordersPerTick.TryGetValue(tick, out var fileOrders)) return;
            foreach (var order in fileOrders.Select(fileOrder => fileOrder.ToOrder()))
            {
                availableOrders.Add(order);
                SendNewOrderMessage(order);

                loggerService.Log($"Load prefabricated Order {order.Id} - From {order.OriginNode.Id} to {order.TargetNode.Id}");
            }
        }

        public void GenerateFile(int maxTicks, string filename)
        {
            OrderGenerator.Run(gridService, maxTicks, filename);
        }

        private static void SendNewOrderMessage(Order order)
        {
            var connectionFactory = new ConnectionFactory { HostName = "localhost" };
            using var connection = connectionFactory.CreateConnection();
            using var channel = connection.CreateModel();

            channel.QueueDeclare(queue: "HahnCargoSim_NewOrders", durable: false, exclusive: false, autoDelete: false, arguments: null);

            JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

            var newOrder = JsonSerializer.Serialize(order.ToDto(), options);

            var body = Encoding.UTF8.GetBytes((newOrder));

            channel.BasicPublish(exchange: string.Empty, routingKey: "HahnCargoSim_NewOrders", basicProperties: null, body: body);
        }

        private void LoadOrdersFromJson()
        {
            var jsonString = File.ReadAllText(config!.OrdersToLoad);
            ordersPerTick = JsonSerializer.Deserialize<Dictionary<int, List<FileOrder>>>(jsonString)!;
        }
    }
}