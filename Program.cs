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

HttpClient httpClient = new();
string PRODUCER_WEBHOOK = "http://localhost:8080/payments/";
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

    Console.WriteLine($"Processing payment {payment.PaymentId}.");
    try
    {
        CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(2));
        destinyProviderResponse = await httpClient
          .PostAsJsonAsync(payment.Webhook, payment, cancellation.Token);

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
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        channel.BasicReject(ea.DeliveryTag, false);
        status = "FAILED";
    }
    var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{PRODUCER_WEBHOOK}{payment.PaymentId}/{status}");
    using (var httpClient = new HttpClient())
    {
        try
        {
            HttpResponseMessage producerResponse = await httpClient.SendAsync(request);
            if (!producerResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to update producer. Status code: {producerResponse.StatusCode}");
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Error sending PATCH request to producer: {ex.Message}");
        }
    }
};

channel.BasicConsume(
    queue: "payments",
    autoAck: false,
    consumer: consumer
);

Console.ReadLine();
