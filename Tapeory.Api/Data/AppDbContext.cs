using Tapeory.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Tapeory.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();

    public DbSet<Template> Templates => Set<Template>();

    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();

    public DbSet<TemplateField> TemplateFields => Set<TemplateField>();

    public DbSet<UploadedFile> UploadedFiles => Set<UploadedFile>();

    public DbSet<TemplateConversionWarning> TemplateConversionWarnings => Set<TemplateConversionWarning>();

    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();

    public DbSet<PrintJobItem> PrintJobItems => Set<PrintJobItem>();

    public DbSet<Printer> Printers => Set<Printer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationSetting>(entity =>
        {
            entity.HasIndex(setting => setting.Key).IsUnique();
            entity.Property(setting => setting.Key).HasMaxLength(200);
        });

        modelBuilder.Entity<Template>(entity =>
        {
            entity.Property(template => template.Name).HasMaxLength(200);
            entity.Property(template => template.Category).HasMaxLength(100);
            entity.Property(template => template.TagsCsv).HasMaxLength(500);

            entity.HasMany(template => template.Versions)
                .WithOne(version => version.Template)
                .HasForeignKey(version => version.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            // The current-version pointer is a separate, optional relationship from the
            // ownership relationship above, so it must not cascade-delete the template.
            entity.HasOne(template => template.CurrentVersion)
                .WithMany()
                .HasForeignKey(template => template.CurrentVersionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(template => template.SourceLbxFile)
                .WithMany()
                .HasForeignKey(template => template.SourceLbxFileId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(template => template.ConversionWarnings)
                .WithOne(warning => warning.Template)
                .HasForeignKey(warning => warning.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TemplateConversionWarning>(entity =>
        {
            entity.Property(warning => warning.Message).HasColumnType("text");
        });

        modelBuilder.Entity<TemplateVersion>(entity =>
        {
            entity.Property(version => version.WidthMm).HasColumnType("decimal(6,2)");
            entity.Property(version => version.HeightMm).HasColumnType("decimal(6,2)");
            entity.Property(version => version.EditorJson).HasColumnType("longtext");

            entity.HasIndex(version => new { version.TemplateId, version.VersionNumber }).IsUnique();

            entity.HasOne(version => version.PreviewImageFile)
                .WithMany()
                .HasForeignKey(version => version.PreviewImageFileId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(version => version.Fields)
                .WithOne(field => field.TemplateVersion)
                .HasForeignKey(field => field.TemplateVersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TemplateField>(entity =>
        {
            entity.Property(field => field.Name).HasMaxLength(100);
            entity.Property(field => field.Label).HasMaxLength(200);
            entity.Property(field => field.DefaultValue).HasMaxLength(500);
        });

        modelBuilder.Entity<UploadedFile>(entity =>
        {
            entity.Property(file => file.FileName).HasMaxLength(260);
            entity.Property(file => file.OriginalFileName).HasMaxLength(260);
            entity.Property(file => file.ContentType).HasMaxLength(200);
            entity.Property(file => file.RelativePath).HasMaxLength(500);
        });

        modelBuilder.Entity<PrintJob>(entity =>
        {
            entity.Property(job => job.PrinterName).HasMaxLength(200);
            entity.Property(job => job.ErrorMessage).HasColumnType("text");

            // Print history should survive a template being removed in the future, so this
            // stays Restrict rather than cascading the job away with it.
            entity.HasOne(job => job.Template)
                .WithMany()
                .HasForeignKey(job => job.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(job => job.Items)
                .WithOne(item => item.PrintJob)
                .HasForeignKey(item => item.PrintJobId)
                .OnDelete(DeleteBehavior.Cascade);

            // A deleted printer shouldn't take its print history with it — PrinterName is
            // already snapshotted onto the job for that case.
            entity.HasOne(job => job.Printer)
                .WithMany()
                .HasForeignKey(job => job.PrinterId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PrintJobItem>(entity =>
        {
            entity.Property(item => item.FieldValuesJson).HasColumnType("text");
            entity.Property(item => item.ErrorMessage).HasColumnType("text");

            entity.HasOne(item => item.RenderedImageFile)
                .WithMany()
                .HasForeignKey(item => item.RenderedImageFileId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Printer>(entity =>
        {
            entity.Property(printer => printer.Name).HasMaxLength(200);
            entity.Property(printer => printer.Model).HasMaxLength(200);
            entity.Property(printer => printer.Address).HasMaxLength(255);
            entity.Property(printer => printer.PrintServerAddress).HasMaxLength(255);
            entity.Property(printer => printer.UsbIdentifier).HasMaxLength(255);
            entity.Property(printer => printer.LabelMediaWidthMm).HasColumnType("decimal(6,2)");
            entity.Property(printer => printer.LabelMediaHeightMm).HasColumnType("decimal(6,2)");
            entity.Property(printer => printer.LastErrorMessage).HasColumnType("text");
        });
    }
}
