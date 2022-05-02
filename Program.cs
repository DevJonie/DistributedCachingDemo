using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

//================================Section 2==================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ProductDbContext>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
//The Decorate extensioin method is from the Scrutor Nuget package
builder.Services.Decorate<IProductRepository, CachedProductRepository>();

builder.Services.AddDistributedMemoryCache();

var app = builder.Build();

app.MapGet("/", async (IProductRepository productRepository) 
    => await productRepository.GetAllAsync());

app.SeedDatabse();
app.Run();

static class HostExtensions
{
    public static void SeedDatabse(this IHost app) 
    {
        var dbContext = app.Services
            .CreateScope().ServiceProvider
            .GetRequiredService<ProductDbContext>();
        if (dbContext is null) return;
        dbContext.AddRange(new List<Product>()
        {
            new() { Id = 1, Name = "Prod 1", Price = 1.2M, },
            new() { Id = 2, Name = "Prod 2", Price = 2.2M, },
            new() { Id = 3, Name = "Prod 3", Price = 3.3M, }
        });
        dbContext.SaveChanges();
    }
}

//================================Section 1==================================

class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public decimal Price { get; set; }
}

class ProductDbContext : DbContext
{
    public DbSet<Product> Products { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseInMemoryDatabase("ProductsCatalog");
}

interface IProductRepository
{
    Task<IEnumerable<Product>> GetAllAsync();
}

class ProductRepository : IProductRepository
{
    private readonly ProductDbContext _dbcontext;

    public ProductRepository(ProductDbContext dbcontext)
        => _dbcontext = dbcontext;

    public async Task<IEnumerable<Product>> GetAllAsync()
        => await _dbcontext.Products
            .AsNoTracking()
            .ToListAsync();
}

class CachedProductRepository : IProductRepository
{
    private const string cacheKey = "PRODUCTS_CACHE_KEY";

    private readonly IProductRepository _productRepository;
    private readonly IDistributedCache _distriutedCache;

    public CachedProductRepository(
        IProductRepository productRepository,
        IDistributedCache distriutedCache)
    {
        _productRepository = productRepository;
        _distriutedCache = distriutedCache;
    }

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        var products = await _distriutedCache.GetAsync<IEnumerable<Product>>(cacheKey);
        if (products is {Count: > 0}) return products;
        products = await _productRepository.GetAllAsync();
        await SetCahceAsync(products);
        return products;

        async Task SetCahceAsync(IEnumerable<Product> products)
        {
            var cacheEntryOptions = new DistributedCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromMinutes(30));
            await _distriutedCache.SetAsync(cacheKey, products, cacheEntryOptions);
        }
    }
}

public static class DistributedCacheExtensions
{
    public async static Task SetAsync<T>(
        this IDistributedCache distributedCache,
        string key, 
        T value, 
        DistributedCacheEntryOptions options, 
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        var serializedObject = JsonSerializer.Serialize(value);
        await distributedCache.SetStringAsync(key, serializedObject, options, cancellationToken);
    }

    public async static Task<T?> GetAsync<T>(
        this IDistributedCache distributedCache, 
        string key, 
        CancellationToken cancellationToken = default)
    {
        var stringResult = await distributedCache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrEmpty(stringResult)) return default;
        return JsonSerializer.Deserialize<T>(stringResult);
    }
}
