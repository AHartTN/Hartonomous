using Hartonomous.Core.Application.Common;
using MediatR;

namespace Hartonomous.Core.Application.Queries.Constants;

/// <summary>
/// Query to get count of all constants.
/// </summary>
public sealed class GetConstantCountQuery : IRequest<Result<long>>
{
}
