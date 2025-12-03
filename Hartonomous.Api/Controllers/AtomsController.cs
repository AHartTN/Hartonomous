using Hartonomous.Shared.Interfaces;
using Hartonomous.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AtomsController : ControllerBase
{
    private readonly IAtomService _atomService;

    public AtomsController(IAtomService atomService)
    {
        _atomService = atomService;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AtomDto>> GetById(long id)
    {
        var atom = await _atomService.GetAtomByIdAsync(id);
        return atom == null ? NotFound() : Ok(atom);
    }

    [HttpGet("hash/{contentHash}")]
    public async Task<ActionResult<AtomDto>> GetByHash(string contentHash)
    {
        var atom = await _atomService.GetAtomByHashAsync(contentHash);
        return atom == null ? NotFound() : Ok(atom);
    }

    [HttpGet("type/{atomType}")]
    public async Task<ActionResult<List<AtomDto>>> GetByType(string atomType, [FromQuery] int skip = 0, [FromQuery] int take = 100)
    {
        var atoms = await _atomService.GetAtomsByTypeAsync(atomType, skip, take);
        return Ok(atoms);
    }

    [HttpPost]
    public async Task<ActionResult<AtomDto>> Create([FromBody] CreateAtomRequest request)
    {
        try
        {
            var atom = await _atomService.CreateAtomAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = atom.Id }, atom);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(long id)
    {
        var result = await _atomService.DeleteAtomAsync(id);
        return result ? NoContent() : NotFound();
    }
}
