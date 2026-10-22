namespace AbonStock.Api.Common.Exceptions;

public class ConcurrencyConflictException(string message) : Exception(message);
