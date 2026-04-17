using System;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Data;

public class MessengerDbContext : DbContext
{
    public MessengerDbContext(DbContextOptions<MessengerDbContext> options)
        : base(options)
    {
    }

    // Simple log of outgoing messages. Add more properties/tables as needed.
    public DbSet<MessageLog> MessageLogs { get; set; } = null!;
}

public class MessageLog
{
    public int Id { get; set; }
    public string Channel { get; set; } = string.Empty; // e.g. Email, SMS, WhatsApp, Push
    public string Recipient { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
