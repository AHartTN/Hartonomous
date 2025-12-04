using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;
using MediatR;
using System.Security.Cryptography;

namespace Hartonomous.Core.Application.Commands.BPETokens;

public sealed class MergeBPETokensCommandHandler : IRequestHandler<MergeBPETokensCommand, Result<MergeBPETokensResponse>>
{
    private readonly IBPETokenRepository _tokenRepository;
    private readonly IConstantRepository _constantRepository;
    private readonly IUnitOfWork _unitOfWork;

    public MergeBPETokensCommandHandler(
        IBPETokenRepository tokenRepository,
        IConstantRepository constantRepository,
        IUnitOfWork unitOfWork)
    {
        _tokenRepository = tokenRepository;
        _constantRepository = constantRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<MergeBPETokensResponse>> Handle(MergeBPETokensCommand request, CancellationToken cancellationToken)
    {
        // Verify all constants exist
        var constants = new List<Constant>();
        foreach (var constantId in request.ConstantSequence)
        {
            var constant = await _constantRepository.GetByIdAsync(constantId, cancellationToken);
            if (constant == null)
            {
                return Result<MergeBPETokensResponse>.Failure($"Constant with ID {constantId} not found");
            }
            constants.Add(constant);
        }

        // Compute hash of the merged sequence
        using var sha256 = SHA256.Create();
        var combinedData = constants.SelectMany(c => c.Data).ToArray();
        var hashBytes = sha256.ComputeHash(combinedData);
        var mergedHash = Hash256.FromBytes(hashBytes);

        // Check if this token already exists
        var existingToken = await _tokenRepository.GetByHashAsync(mergedHash, cancellationToken);
        if (existingToken != null)
        {
            // Increment frequency
            existingToken.IncrementFrequency();
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<MergeBPETokensResponse>.Success(new MergeBPETokensResponse
            {
                TokenId = existingToken.Id,
                TokenHash = existingToken.Hash.ToString(),
                MergeLevel = existingToken.MergeLevel,
                SequenceLength = existingToken.SequenceLength
            });
        }

        // Determine merge level (max level of constituents + 1)
        var maxMergeLevel = 0; // Constants are level 0
        // Note: If we later support merging tokens with tokens, we'd query their levels here

        // Get next token ID
        var nextTokenId = await _tokenRepository.GetNextTokenIdAsync(cancellationToken);
        
        // Create new BPE token from constant sequence
        var token = BPEToken.CreateFromConstantSequence(
            tokenId: nextTokenId,
            constantSequence: request.ConstantSequence,
            hash: mergedHash,
            mergeLevel: maxMergeLevel + 1,
            constants: constants
        );

        await _tokenRepository.AddAsync(token, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<MergeBPETokensResponse>.Success(new MergeBPETokensResponse
        {
            TokenId = token.Id,
            TokenHash = token.Hash.ToString(),
            MergeLevel = token.MergeLevel,
            SequenceLength = token.SequenceLength
        });
    }
}
