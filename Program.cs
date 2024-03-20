// See https://aka.ms/new-console-template for more information
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PaymentConsumer.DTOs;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

Console.WriteLine("Hello, World!");

ConnectionFactory factory = new()
{
    HostName = "localhost",
    Port = 5672,
    UserName = "admin",
    Password = "admin"
};

var connection = factory.CreateConnection();
var channel = connection.CreateModel();

channel.QueueDeclare(
    queue: "payments",
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null
);

EventingBasicConsumer consumer = new(channel);

Console.WriteLine("Waiting for messages.");

consumer.Received += async (model, ea) =>
{
    HttpClient httpClient = new();

    byte[] body = ea.Body.ToArray();
    var message = Encoding.UTF8.GetString(body);
    var payment = JsonSerializer.Deserialize<Payment>(message);

    if (payment == null)
    {
        channel.BasicReject(ea.DeliveryTag, false);
        return;
    }

    string status;
    HttpResponseMessage destinyProviderResponse;
    string PSP_WEBHOOK = "http://localhost:5039/payments/pix";
    string PRODUCER_WEBHOOK = "http://localhost:8080/payments/";

    Console.WriteLine($"Processing payment {payment.PaymentId}.");
    try
    {
        CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(2));
        destinyProviderResponse = await httpClient
          .PostAsJsonAsync(PSP_WEBHOOK, payment, cancellation.Token);

        if (destinyProviderResponse.IsSuccessStatusCode)
        {
            Console.WriteLine("SUCCESS");
            channel.BasicAck(ea.DeliveryTag, false);
            status = "SUCCESS";
        }
        else
        {
            Console.WriteLine("FAILED");
            channel.BasicReject(ea.DeliveryTag, false);
            status = "FAILED";
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Error: Timeout");

        channel.BasicReject(ea.DeliveryTag, false);
        status = "FAILED";
    }

    var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{PRODUCER_WEBHOOK}{payment.PaymentId}/{status}");

    await httpClient.SendAsync(request);
    await httpClient.PatchAsJsonAsync(PSP_WEBHOOK, new TransferStatusDTO { Status = status, Id = payment.PaymentId });
};

channel.BasicConsume(
    queue: "payments",
    autoAck: false,
    consumer: consumer
);

Console.ReadLine();
