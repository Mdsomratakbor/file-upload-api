using file_upload_api.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace file_upload_api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<UploadedFile> UploadedFiles { get; set; }
    }

}
