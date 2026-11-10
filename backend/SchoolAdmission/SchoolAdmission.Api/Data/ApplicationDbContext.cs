using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Entities;

namespace SchoolAdmission.Api.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<SchoolClass> Classes => Set<SchoolClass>();
        public DbSet<Student> Students => Set<Student>();
        public DbSet<Hold> Holds => Set<Hold>();


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SchoolClass>(e =>
            {
                e.ToTable("classes", t =>
                    t.HasCheckConstraint("ck_classes_total_seats", "\"TotalSeats\" > 0"));
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired().HasMaxLength(200);
                e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            });

            modelBuilder.Entity<Student>(e =>
            {
                e.ToTable("students");
                e.HasKey(x => x.Id);
                e.Property(x => x.BirthRegistrationNumber)
                    .IsRequired().HasMaxLength(17).IsFixedLength();
                e.HasIndex(x => x.BirthRegistrationNumber).IsUnique();
                e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            });

            modelBuilder.Entity<Hold>(e =>
            {
                e.ToTable("holds");
                e.HasKey(x => x.Id);

                e.Property(x => x.Status)
                    .HasConversion<string>()   // stored as text, readable in the DB
                    .HasMaxLength(20)
                    .IsRequired();

                e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
                e.Property(x => x.ExpiresAt).HasColumnType("timestamptz");
                e.Property(x => x.ConfirmedAt).HasColumnType("timestamptz");

                e.HasOne(x => x.Class)
                    .WithMany(c => c.Holds)
                    .HasForeignKey(x => x.ClassId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Student)
                    .WithMany(s => s.Holds)
                    .HasForeignKey(x => x.StudentId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Fast seat counting per class
                e.HasIndex(x => new { x.ClassId, x.Status, x.ExpiresAt });

                // Fast "my holds" lookup
                e.HasIndex(x => x.StudentId);

                // Safety net: a student can have at most ONE Held-or-Confirmed row
                e.HasIndex(x => x.StudentId)
                    .IsUnique()
                    .HasFilter("\"Status\" IN ('Held', 'Confirmed')")
                    .HasDatabaseName("ux_holds_student_active");
            });
        }
    }
}
