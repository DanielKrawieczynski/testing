namespace Ecommerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public interface IEcommerceRepository
{
    void SaveChanges();
    Order FindOrder(int orderId);
}

public class EcommerceRepository : IEcommerceRepository
{
    private readonly EcommerceDbContext _context;

    public EcommerceRepository(EcommerceDbContext context)
    {
        _context = context;
    }

    public void SaveChanges()
    {
        _context.SaveChanges();
    }

    public Order FindOrder(int orderId)
    {
        return _context.Orders.Find(orderId);
    }
}

public interface IUserProvider
{
    string GetUserName();
    bool IsAdmin();
}

public class UserProvider : IUserProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetUserName()
    {
        return _httpContextAccessor.HttpContext.User.Identity.Name;
    }

    public bool IsAdmin()
    {
        var user = _httpContextAccessor.HttpContext.User;
        return user.IsInRole("Administrator");
    }
}

public class OrderService
{
    private readonly IEcommerceRepository _repository;
    private readonly IUserProvider _userProvider;

    public OrderService(IEcommerceRepository repository, IUserProvider userProvider)
    {
        _repository = repository;
        _userProvider = userProvider;
    }

    public void PlaceOrder(int orderId)
    {
        var order = _repository.FindOrder(orderId);

        if (order == null)
        {
            throw new ArgumentException("Order not found", nameof(orderId));
        }

        if (order.Status != OrderStatus.Draft)
        {
            throw new InvalidOperationException("Order must be in Draft status to place the order");
        }

        if (order.Items.Count == 0)
        {
            throw new InvalidOperationException("Order must have at least one item");
        }

        var userId = _userProvider.GetUserName();
        if (!_userProvider.IsAdmin() && order.CustomerId != userId)
        {
            throw new InvalidOperationException("Order can only be placed by the same customer or an administrator");
        }

        Money totalValue = Money.Zero;
        foreach (var item in order.Items)
        {
            Money itemValue = item.Price * item.Quantity;
            totalValue += itemValue;
        }

        if (order.IsVipCustomer)
        {
            Money discount = totalValue * 0.1m;
            totalValue -= discount;
        }

        order.TotalValue = totalValue;
        order.Status = OrderStatus.Placed;

        _repository.SaveChanges();

        var mailService = new MailService();
        mailService.SendOrderConfirmation(order);

        MessageBus.Publish(new OrderPlacedMessage(order.OrderId));
    }
}

public class Money
{
    public static Money Zero => new Money(0);

    public decimal Amount { get; }

    public Money(decimal amount)
    {
        Amount = amount;
    }

    public Money Add(Money other)
    {
        decimal result = Amount + other.Amount;
        return new Money(result);
    }

    public static Money operator *(Money money, int multiplier)
    {
        decimal result = money.Amount * multiplier;
        return new Money(result);
    }

    public static Money operator *(int multiplier, Money money)
    {
        return money * multiplier;
    }

    public static Money operator +(Money money1, Money money2)
    {
        decimal result = money1.Amount + money2.Amount;
        return new Money(result);
    }

    public static Money operator *(Money money, decimal multiplier)
    {
        decimal result = money.Amount * multiplier;
        return new Money(result);
    }

    public static Money operator *(decimal multiplier, Money money)
    {
        return money * multiplier;
    }

    public static Money operator -(Money money1, Money money2)
    {
        decimal result = money1.Amount - money2.Amount;
        return new Money(result);
    }
}


public class MailService
{
    public void SendOrderConfirmation(Order order)
    {
        Console.WriteLine($"Sending order confirmation e-mail for order with ID {order.OrderId}");
    }
}

public static class MessageBus
{
    public static void Publish(object message)
    {
        Console.WriteLine($"Publishing message: {message}");
    }
}

public class OrderPlacedMessage
{
    public int OrderId { get; }

    public OrderPlacedMessage(int orderId)
    {
        OrderId = orderId;
    }
}

public class EcommerceDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }

    public EcommerceDbContext(DbContextOptions<EcommerceDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseInMemoryDatabase("ecommerce");
    }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>()
             .HasKey(o => o.OrderId);

        modelBuilder.Entity<Order>()
            .Property(o => o.OrderId)
            .ValueGeneratedOnAdd();

        modelBuilder.Entity<Order>()
            .Property(o => o.CustomerId)
            .IsRequired();

        modelBuilder.Entity<Order>()
            .Property(o => o.Status)
            .IsRequired();

        modelBuilder.Entity<Order>()
            .Property(o => o.TotalValue)
            .IsRequired()
            .HasConversion(m => m.Amount, a => new Money(a));

        modelBuilder.Entity<OrderItem>()
            .HasKey(oi => oi.OrderItemId);

        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.OrderItemId)
            .ValueGeneratedOnAdd();

        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.ProductId)
            .IsRequired();

        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.Price)
            .IsRequired()
            .HasConversion(m => m.Amount, a => new Money(a));
    }
}

public class Order
{
    public int OrderId { get; set; }
    public string CustomerId { get; set; }
    public OrderStatus Status { get; set; }
    public Money TotalValue { get; set; }
    public bool IsVipCustomer { get; set; }
    public List<OrderItem> Items { get; set; }
}

public enum OrderStatus
{
    Draft,
    Placed,
    Shipped,
    Delivered
}

public class OrderItem
{
    public int OrderItemId { get; set; }
    public int ProductId { get; set; }
    public Money Price { get; set; }
    public int Quantity { get; set; }
}

