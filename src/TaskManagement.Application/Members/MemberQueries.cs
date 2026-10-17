using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Common;
using TaskManagement.Application.Identity;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Identity;

namespace TaskManagement.Application.Members;

public sealed class MemberQueries(IAppDbContext db)
{
    public async Task<PagedResult<MemberDto>> ListAsync(MemberListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // EF Core translates Contains to LIKE and escapes % and _ in the search text.
            var search = query.Search.Trim().ToLowerInvariant();
            users = users.Where(u =>
                u.FullName.ToLower().Contains(search)
                || u.Email.ToLower().Contains(search)
                || (u.EmployeeCode != null && u.EmployeeCode.ToLower().Contains(search)));
        }

        if (query.IsActive is { } isActive)
        {
            users = users.Where(u => u.IsActive == isActive);
        }

        if (query.RoleId is { } roleId)
        {
            users = users.Where(u => u.GlobalRoleId == roleId);
        }

        var total = await users.CountAsync(cancellationToken);
        var items = await Project(users.OrderBy(u => u.FullName).ThenBy(u => u.Id).Skip(query.Page.Skip).Take(query.Page.SafePageSize))
            .ToListAsync(cancellationToken);
        return new PagedResult<MemberDto>(items, query.Page.SafePage, query.Page.SafePageSize, total);
    }

    public async Task<Result<MemberDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var member = await Project(db.Users.AsNoTracking().Where(u => u.Id == id)).FirstOrDefaultAsync(cancellationToken);
        return member is null ? CommonErrors.NotFound("Member") : member;
    }

    private IQueryable<MemberDto> Project(IQueryable<User> users) =>
        users.Select(u => new MemberDto(
            u.Id,
            u.Email,
            u.FullName,
            u.TimeZone,
            u.EmployeeCode,
            db.Roles.Where(r => r.Id == u.GlobalRoleId).Select(r => new RoleRef(r.Id, r.Name)).First(),
            u.IsActive,
            u.PasswordHash != null,
            u.CreatedAt,
            u.LastSignInAt,
            u.DeactivatedAt,
            u.Version));
}
