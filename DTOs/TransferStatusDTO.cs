using System.ComponentModel.DataAnnotations;

namespace PaymentConsumer.DTOs;
public class TransferStatusDTO()
{
    [RegularExpression(@"^(?:SUCCESS|FAILED)$")]
    public required string Status { get; set; }
    public required int Id { get; set; }
}