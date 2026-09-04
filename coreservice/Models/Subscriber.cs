namespace coreservice.Models;

public class Subscriber
{
    public int Id { get; set; }
    public string PhoneNumber { get; set; } = "";
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; }
}
