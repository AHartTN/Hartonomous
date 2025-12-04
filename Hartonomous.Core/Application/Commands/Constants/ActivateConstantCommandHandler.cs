using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Enums;
using MediatR;

namespace Hartonomous.Core.Application.Commands.Constants;

public sealed class ActivateConstantCommandHandler : IRequestHandler<ActivateConstantCommand, Result>
{
    private readonly IConstantRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public ActivateConstantCommandHandler(IConstantRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ActivateConstantCommand request, CancellationToken cancellationToken)
    {
        var constant = await _repository.GetByIdAsync(request.ConstantId, cancellationToken);

        if (constant == null)
        {
            return Result.Failure($"Constant with ID {request.ConstantId} not found");
        }

        if (constant.Status != ConstantStatus.Projected)
        {
            return Result.Failure($"Constant must be in Projected status to be activated. Current status: {constant.Status}");
        }

        constant.Activate();

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
