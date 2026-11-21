// Controllers for the F1 PoC: same controller name and action name in different namespaces/areas.
// Admin requires authorization and is keyed per user; Public is anonymous and takes a free string.

namespace BoricuaCoder.API.CoreSetup.Tests.Caching.Poc.Admin
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    [Area("Admin")]
    [Authorize]
    public sealed class UsersController : ControllerBase
    {
        public IActionResult Get(int id) => Ok(id);
    }
}

namespace BoricuaCoder.API.CoreSetup.Tests.Caching.Poc.Public
{
    using Microsoft.AspNetCore.Mvc;

    public sealed class UsersController : ControllerBase
    {
        public IActionResult Get(int id, string? lang) => Ok(id);
    }
}
