using MediatR;

namespace Hartonomous.Core.Application.Common;

/// <summary>
/// Marker interface for queries that return a value
/// </summary>
/// <typeparam name="TResponse">The response type</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>
{
}
