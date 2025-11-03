using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrometheusGrafanaSampleApi.Data;
using PrometheusGrafanaSampleApi.Models;

namespace PrometheusGrafanaSampleApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TodosController : ControllerBase
{
    private readonly TodoContext _context;
    private readonly ILogger<TodosController> _logger;

    public TodosController(TodoContext context, ILogger<TodosController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // GET: api/todos
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TodoItem>>> GetTodos()
    {
        _logger.LogInformation("Getting all todos");
        var todos = await _context.Todos.ToListAsync();
        return Ok(todos);
    }

    // GET: api/todos/5
    [HttpGet("{id}")]
    public async Task<ActionResult<TodoItem>> GetTodo(int id)
    {
        _logger.LogInformation("Getting todo with id: {Id}", id);
        var todo = await _context.Todos.FindAsync(id);

        if (todo == null)
        {
            _logger.LogWarning("Todo with id {Id} not found", id);
            return NotFound();
        }

        return Ok(todo);
    }

    // POST: api/todos
    [HttpPost]
    public async Task<ActionResult<TodoItem>> PostTodo(TodoItem todo)
    {
        _logger.LogInformation("Creating new todo: {Title}", todo.Title);
        
        if (string.IsNullOrWhiteSpace(todo.Title))
        {
            _logger.LogWarning("Attempted to create todo with empty title");
            return BadRequest("Title is required");
        }

        _context.Todos.Add(todo);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created todo with id: {Id}", todo.Id);
        return CreatedAtAction(nameof(GetTodo), new { id = todo.Id }, todo);
    }

    // PUT: api/todos/5
    [HttpPut("{id}")]
    public async Task<IActionResult> PutTodo(int id, TodoItem todo)
    {
        if (id != todo.Id)
        {
            _logger.LogWarning("Mismatched id in PutTodo: {Id} != {TodoId}", id, todo.Id);
            return BadRequest("Id mismatch");
        }

        _logger.LogInformation("Updating todo with id: {Id}", id);

        if (string.IsNullOrWhiteSpace(todo.Title))
        {
            _logger.LogWarning("Attempted to update todo with empty title");
            return BadRequest("Title is required");
        }

        _context.Entry(todo).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Updated todo with id: {Id}", id);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!TodoExists(id))
            {
                _logger.LogWarning("Todo with id {Id} not found for update", id);
                return NotFound();
            }
            throw;
        }

        return NoContent();
    }

    // DELETE: api/todos/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTodo(int id)
    {
        _logger.LogInformation("Deleting todo with id: {Id}", id);
        
        var todo = await _context.Todos.FindAsync(id);
        if (todo == null)
        {
            _logger.LogWarning("Todo with id {Id} not found for deletion", id);
            return NotFound();
        }

        _context.Todos.Remove(todo);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted todo with id: {Id}", id);
        return NoContent();
    }

    private bool TodoExists(int id)
    {
        return _context.Todos.Any(e => e.Id == id);
    }
}

