using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using nexRemote.Server.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nexRemote.Server.Data
{
    public class TestingDbContext : AppDb
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseInMemoryDatabase("nexRemote");
            base.OnConfiguring(options);
        }
    }
}
