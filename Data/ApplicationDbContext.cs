using Microsoft.EntityFrameworkCore;
using ToolDrawingProcessor.Models;

namespace ToolDrawingProcessor.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<UploadedFile> UploadedFiles => Set<UploadedFile>();
    public DbSet<ToolSpecification> ToolSpecifications => Set<ToolSpecification>();
    public DbSet<AIExtractionLog> AIExtractionLogs => Set<AIExtractionLog>();
    public DbSet<CorrectionFeedback> CorrectionFeedbacks => Set<CorrectionFeedback>();
    public DbSet<PromptMemory> PromptMemories => Set<PromptMemory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UploadedFile>(entity =>
        {
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.UploadedAt);
        });

        modelBuilder.Entity<ToolSpecification>(entity =>
        {
            entity.HasOne(e => e.UploadedFile)
                  .WithOne(e => e.ToolSpecification)
                  .HasForeignKey<ToolSpecification>(e => e.UploadedFileId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AIExtractionLog>(entity =>
        {
            entity.HasOne(e => e.UploadedFile)
                  .WithOne(e => e.AIExtractionLog)
                  .HasForeignKey<AIExtractionLog>(e => e.UploadedFileId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CorrectionFeedback>(entity =>
        {
            entity.HasOne(e => e.UploadedFile)
                  .WithMany(e => e.CorrectionFeedbacks)
                  .HasForeignKey(e => e.UploadedFileId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.FieldName);
            entity.HasIndex(e => e.WasCorrect);
        });

        modelBuilder.Entity<PromptMemory>(entity =>
        {
            entity.HasIndex(e => e.UsageCount);
        });
    }
}
