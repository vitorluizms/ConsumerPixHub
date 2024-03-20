namespace PaymentConsumer.DTOs;
public class Payment
{
    public required int PaymentId { get; set; }
    public required Origin Origin { get; set; } 
    public required Destiny Destiny { get; set; }
    public required int Amount { get; set; }
    public string? Description { get; set; }
}