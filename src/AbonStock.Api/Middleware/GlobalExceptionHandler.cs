using AbonStock.Api.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AbonStock.Api.Middleware;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = CreateProblemDetails(httpContext, exception);

        if (problemDetails.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Internal error resulting in HTTP {StatusCode}: {Title} - {Detail}",
                problemDetails.Status, problemDetails.Title, problemDetails.Detail);
        }
        else
        {
            logger.LogWarning(exception, "Handled client exception resulting in HTTP {StatusCode}: {Title} - {Detail}",
                problemDetails.Status, problemDetails.Title, problemDetails.Detail);
        }

        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static ProblemDetails CreateProblemDetails(HttpContext httpContext, Exception exception)
    {
        return exception switch
        {
            NotFoundException ex => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = ex.Message,
                Instance = httpContext.Request.Path
            },

            ConflictException ex => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = ex.Message,
                Instance = httpContext.Request.Path
            },

            ConcurrencyConflictException ex => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Concurrency Conflict",
                Detail = ex.Message,
                Instance = httpContext.Request.Path
            },

            DbUpdateConcurrencyException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Concurrency Conflict",
                Detail = "The resource was modified by another operation. Please reload and retry.",
                Instance = httpContext.Request.Path
            },

            ValidationException ex => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Failed",
                Detail = ex.Message,
                Instance = httpContext.Request.Path
            },

            DbUpdateException dbEx when dbEx.InnerException is PostgresException pgEx =>
                HandlePostgresException(httpContext, pgEx),

            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred. Please try again later.",
                Instance = httpContext.Request.Path
            }
        };
    }

    private static ProblemDetails HandlePostgresException(HttpContext httpContext, PostgresException pgEx)
    {
        if (pgEx.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            var detail = pgEx.ConstraintName switch
            {
                "IX_Products_Sku" => "A product with this SKU already exists.",
                "IX_InventoryBatches_ProductId_BatchNo" => "A batch with this BatchNo already exists for this product.",
                "IX_OrderItems_OrderId_ProductId" => "Duplicate product in order items is not allowed.",
                _ => "A record with this unique value already exists."
            };

            return new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Unique Constraint Violation",
                Detail = detail,
                Instance = httpContext.Request.Path
            };
        }

        if (pgEx.SqlState == PostgresErrorCodes.CheckViolation)
        {
            var detail = pgEx.ConstraintName switch
            {
                "CK_Products_UnitPrice_NonNegative" => "Product unit price must be non-negative.",
                "CK_InventoryBatches_PhysicalQuantity_NonNegative" => "Batch physical quantity cannot be negative.",
                "CK_InventoryBatches_InitialQuantity_Positive" => "Batch initial quantity must be positive.",
                "CK_InventoryBatches_ReservedQuantity_Valid" => "Reserved quantity must be non-negative and cannot exceed physical quantity.",
                "CK_OrderItems_Quantity_Positive" => "Order item quantity must be greater than zero.",
                "CK_OrderReservations_Quantity_Positive" => "Reservation quantity must be greater than zero.",
                _ => "A database check constraint was violated."
            };

            return new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invariant Violation",
                Detail = detail,
                Instance = httpContext.Request.Path
            };
        }

        if (pgEx.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Reference",
                Detail = "The operation references a record that does not exist or is constrained by existing references.",
                Instance = httpContext.Request.Path
            };
        }

        return new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Database Error",
            Detail = "An unexpected database error occurred.",
            Instance = httpContext.Request.Path
        };
    }
}
