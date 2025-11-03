using Microsoft.EntityFrameworkCore;
using PrometheusGrafanaSampleApi.Models;

namespace PrometheusGrafanaSampleApi.Data;

public class TodoContext : DbContext
{
    public TodoContext(DbContextOptions<TodoContext> options) : base(options)
    {
    }

    public DbSet<TodoItem> Todos { get; set; }
}
