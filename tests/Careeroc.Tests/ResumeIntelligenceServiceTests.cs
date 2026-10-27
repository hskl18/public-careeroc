using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Contracts.Resumes;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using System.Text.Json;

namespace Careeroc.Tests;

public sealed class ResumeIntelligenceServiceTests
{
    [Fact]
    public async Task CreateResumeAsync_CreatesVersionAndEvaluation()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName,
            TargetRole = "Software engineering intern"
        };
        dbContext.Users.Add(user);
        dbContext.JobApplications.Add(
            new JobApplication
            {
                User = user,
                UserId = user.Id,
                Company = "Roblox",
                Role = "Software Engineering Intern",
                Status = ApplicationStatus.Interviewing
            });
        await dbContext.SaveChangesAsync();

        var service = new ResumeIntelligenceService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var response = await service.CreateResumeAsync(
            new CreateResumeRequest(
                "SWE resume",
                "text",
                """
                Alex Rivera
                alex@example.com
                https://github.com/alexrivera
                Education: Cascadia State Bachelor of Science Computer Science
                Experience: Software Engineer Intern 2025 built React TypeScript APIs with PostgreSQL and Docker.
                Project: Careeroc built Next.js C# .NET PostgreSQL Gmail automation with Gemma Ollama LLM agents.
                Skills: TypeScript, React, Next.js, C#, .NET, SQL, PostgreSQL, Docker, AWS, Python, API, LLM
                """),
            ApplicationDefaults.DefaultUserEmail);

        var document = Assert.Single(response.Documents);
        Assert.Equal("SWE resume", document.Title);
        Assert.NotNull(document.LatestVersion);
        Assert.NotNull(document.LatestEvaluation);
        Assert.True(document.LatestVersion!.Skills.Count >= 8);
        Assert.True(document.LatestEvaluation!.Score >= 80);
        Assert.Equal(1, document.LatestEvaluation.Evidence.ApplicationCount);
        Assert.Equal(1, document.LatestEvaluation.Evidence.InterviewOrOfferCount);
        var memory = Assert.Single(dbContext.UserMemoryEntries.ToList());
        Assert.Equal("resume", memory.Kind);
        Assert.Equal("resume.latest_evaluation", memory.Key);
        Assert.Equal("confirmed", memory.ConfirmationStatus);
        Assert.Contains("SWE resume", memory.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReextractResumeAsync_AddsANewVersionToAnExistingResume()
    {
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var clock = new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));
        var service = new ResumeIntelligenceService(dbContext, clock);
        var created = await service.CreateResumeAsync(
            new CreateResumeRequest(
                "SWE resume",
                "text",
                """
                Alex Rivera
                alex@example.com
                Experience: Software Engineer Intern 2025 built React TypeScript APIs with PostgreSQL and Docker.
                Skills: TypeScript, React, C#, .NET, SQL, PostgreSQL, Docker
                """),
            ApplicationDefaults.DefaultUserEmail);

        var reextracted = await service.ReextractResumeAsync(
            Assert.Single(created.Documents).Id,
            ApplicationDefaults.DefaultUserEmail);

        Assert.NotNull(reextracted);
        Assert.Equal(2, dbContext.ResumeVersions.Count());
        Assert.Equal(2, dbContext.ResumeEvaluations.Count());
    }

    [Fact]
    public async Task CreateResumeAsync_ReturnsStructuredSectionsWithoutProjectBlob()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new ResumeIntelligenceService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var response = await service.CreateResumeAsync(
            new CreateResumeRequest(
                "Flattened PDF resume",
                "pdf",
                """
                Jordan Lee Berkeley, CA | jordanlee@example.edu EDUCATION Westbrook University Sep 2021 - Jun 2025 B.S. in Computer Science GPA: 3.6 WORK EXPERIENCE Westbrook CS Department Jan 2026 - Present Teaching Assistant Berkeley, CA • Supported instruction for an introductory databases course. • Led discussions and office hours on SQL and query planning. Research Assistant @ Harbor Vision Lab Dec 2022 - June 2023 • Evaluated multimodal LLMs for image generation and visual understanding tasks. TECHNICAL PROJECTS Inbox Copilot Mar 2026 • Designed a Gmail-first recruiting operations workspace for job seekers. • Implemented a multi-agent orchestration pipeline with Gmail API sync and evidence synthesis. Trailmap Winter 2026 • Built an AI trip-planning web app from uploaded trail photos. Pixel Garden Fall 2023 • Built a multiplayer browser game with map editing tools. SKILLS Python, TypeScript, Next.js, React, SQL, OpenAI, RAG
                """),
            ApplicationDefaults.DefaultUserEmail);

        var structured = response.Documents.Single().LatestVersion!.StructuredData;

        Assert.True(structured.Experience.Count >= 2);
        Assert.NotEmpty(structured.Education);
        Assert.True(structured.Projects.Count >= 3);
        Assert.Contains(structured.Experience, experience =>
            experience.Company.Contains("Harbor Vision Lab", StringComparison.OrdinalIgnoreCase) &&
            experience.Title.Contains("Research Assistant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(structured.Projects, project => project.Name.Contains("Inbox Copilot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(structured.Projects, project => project.Name.Contains("Trailmap", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(structured.Projects, project => project.Name.Contains("Pixel Garden", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(structured.Projects, project => project.Description.Contains("WORK EXPERIENCE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateResumeAsync_FlagsStructuredWeaknessesWhenParsingLooksMessy()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new ResumeIntelligenceService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var response = await service.CreateResumeAsync(
            new CreateResumeRequest(
                "Messy resume",
                "text",
                """
                Alex Rivera Portland, OR | (555) 010 - 0142 | demo@example.com | Portfolio | GitHub | LinkedIn
                EDUCATION Lakeview Institute of Technology M.S Computer Science
                WORK EXPERIENCE
                Harbor Civic Services Software Engineer Salem, OR Oct 2023 - Oct 2025
                • Delivered serverless workflow tools using AWS Lambda/API Gateway, reducing manual operations by 60+%.
                Brightpath Learning LLC Software Engineer Intern Eugene, OR Jun 2024 - Feb 2025
                • Developed a responsive enrollment platform. Data Engineer Intern Eugene, OR
                TECHNICAL PROJECTS
                Trailmap Trip Planner | Live Demo | §
                Built an AI travel-planning web app.
                SKILLS typescript, python, java, sql, react, next.js, aws, docker
                """),
            ApplicationDefaults.DefaultUserEmail);

        var evaluation = response.Documents.Single().LatestEvaluation!;
        var structured = response.Documents.Single().LatestVersion!.StructuredData;
        var allWeaknesses = evaluation.SectionReviews
            .SelectMany(review => review.Weaknesses)
            .ToList();

        Assert.Equal("Alex Rivera", structured.Contact.Name);
        Assert.Equal("Portland, OR", structured.Contact.Location);
        Assert.DoesNotContain(structured.Projects, project => project.Name.Contains('§'));
        Assert.Contains(evaluation.Gaps, gap => gap.Contains("profile", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(allWeaknesses, weakness => weakness.Contains("role header", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(allWeaknesses, weakness => weakness.Contains("canonical casing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateResumeAsync_RejectsTooShortText()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new ResumeIntelligenceService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateResumeAsync(
                new CreateResumeRequest("Short", "text", "too short"),
                ApplicationDefaults.DefaultUserEmail));

        Assert.Equal("Paste at least 80 characters of resume text.", exception.Message);
    }

    [Fact]
    public async Task ImportResumeFileAsync_CreatesResumeFromTextFile()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new ResumeIntelligenceService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var sourceText = """
            Alex Rivera
            alex@example.com
            Education: Cascadia State Bachelor of Science Computer Science
            Experience: Software Engineer Intern 2025 built React TypeScript APIs.
            Project: Careeroc built Next.js C# .NET PostgreSQL Gmail automation.
            Skills: TypeScript, React, Next.js, C#, .NET, SQL, PostgreSQL, Docker
            """;
        var response = await service.ImportResumeFileAsync(
            new ImportResumeFileRequest(
                null,
                "alex-resume.txt",
                "text/plain",
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sourceText))),
            ApplicationDefaults.DefaultUserEmail);

        var document = Assert.Single(response.Documents);
        Assert.Equal("alex-resume", document.Title);
        Assert.Equal("text", document.SourceKind);
        Assert.Equal("alex-resume.txt", document.SourceFileName);
        Assert.True(document.RawSourceCharacterCount >= 80);
        Assert.Equal("initial_import", document.LatestVersion!.ExtractionMetadata.ExtractionMethod);
        Assert.True(document.LatestEvaluation!.Score > 0);
    }

    [Fact]
    public async Task CreateResumeAsync_ExtractsDensePdfStyleResumeIntoStructuredDossier()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new ResumeIntelligenceService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var response = await service.CreateResumeAsync(
            new CreateResumeRequest(
                "Dense PDF resume",
                "pdf",
                """
                Candidate Name
                Portland, OR | candidate@example.com | (555) 010-0137 | # Portfolio | § GitHub | ï LinkedIn | US Citizen
                EDUCATION
                Lakeview Institute of Technology Sep 2026 - Jun 2028 M.S. in Computer Science (Incoming)
                Cascadia State University Sep 2024 - Jun 2026 B.S. in Computer Science and Engineering
                WORK EXPERIENCE
                Harbor Civic Services Oct 2023 - Oct 2025 Software Engineer Salem, OR
                • Built a hu-
                  man review dashboard using AWS Lambda/API Gateway, SOQL, Salesforce, Twilio, and SNS.
                • Integrated a classifier to triage workers' requests and reduce manual queue handling.
                Brightpath Learning LLC Jun 2024 - Feb 2025 Software Engineer Intern and Data Engineer Intern Eugene, OR
                • Built serverless microservices for 80+ schools and 40K+ courses.
                • Loaded 30K+ documents through AWS Glue, S3, and RDS for downstream analytics.
                Northwind Cloud Sep 2023 - Dec 2023 Cloud Engineer Intern Seattle, WA
                • Automated CloudFormation, Lambda, API Gateway, and DynamoDB runbooks.
                TECHNICAL PROJECTS
                Inbox Copilot | Recruiting Operations Workspace Mar 2026
                • Built a Gmail-first recruiting operations workspace with multimodal inference and agentic workflows.
                Trailmap Trip Planner | Live Demo Mar 2026
                • Built an AI travel-planning app with React, Next.js, PostgreSQL, and Vercel.
                Two-Stage Sequential Recommendation System | Podcast Recommender Pipeline Dec 2025
                • Implemented a PyTorch and Transformers recommendation pipeline.
                SKILLS
                Languages: Python, TypeScript/JavaScript, SQL, C#, Java, C/C++, Go, Rust
                Full Stack: React, Next.js, Node.js, ASP.NET Core/.NET 8, REST APIs, PostgreSQL, EF Core, Tailwind CSS
                Cloud & DevOps: AWS, Docker, Kubernetes, GitHub Actions, Linux, Vercel, Cloudflare
                Data & AI: ETL, microservices, PyTorch, Transformers, Ollama, multimodal inference, agentic workflows
                """),
            ApplicationDefaults.DefaultUserEmail);

        var document = Assert.Single(response.Documents);
        var version = document.LatestVersion!;
        var structured = version.StructuredData;
        var storedSource = dbContext.ResumeDocuments.Single().SourceText;

        Assert.Equal("pdf", document.SourceKind);
        Assert.Equal("pdf", version.ExtractionMetadata.SourceKind);
        Assert.Equal("initial_import", version.ExtractionMetadata.ExtractionMethod);
        Assert.Equal(ResumePromptVersions.DeterministicExtraction, version.ExtractionMetadata.PromptVersion);
        Assert.True(document.RawSourceCharacterCount > 1_500);
        Assert.Contains("human review dashboard", storedSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hu-\n", storedSource, StringComparison.Ordinal);
        Assert.Equal("candidate@example.com", structured.Contact.Email);
        Assert.Equal("Portland, OR", structured.Contact.Location);
        Assert.Contains(structured.Education, education => education.School == "Lakeview Institute of Technology");
        Assert.Contains(structured.Education, education => education.School == "Cascadia State University");
        Assert.Contains(structured.Experience, experience => experience.Company == "Harbor Civic Services");
        Assert.Contains(structured.Experience, experience => experience.Company == "Brightpath Learning LLC");
        Assert.Contains(structured.Experience, experience => experience.Company == "Northwind Cloud");
        Assert.Contains(structured.Projects, project => project.Name.Contains("Inbox Copilot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(structured.Projects, project => project.Name.Contains("Trailmap", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(structured.Projects, project => project.Name.Contains("Two-Stage Sequential Recommendation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(version.Skills, skill => skill.Equals("typescript", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(version.Skills, skill => skill.Equals("python", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(version.Skills, skill => skill.Equals("aws", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(version.Skills, skill => skill.Equals("docker", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetWorkspaceAsync_NormalizesPersistedStructuredDataWithNullSections()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var now = new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName,
            TargetRole = "Software engineering intern"
        };
        var document = new ResumeDocument
        {
            User = user,
            UserId = user.Id,
            Title = "Partially malformed resume",
            SourceKind = "text",
            SourceText = """
                Alex Rivera
                alex@example.com
                Experience: Software Engineer Intern building backend APIs.
                Project: Careeroc resume intelligence and inbox workflow.
                Skills: TypeScript, C#, PostgreSQL
                """,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        document.Versions.Add(
            new ResumeVersion
            {
                ResumeDocument = document,
                ResumeDocumentId = document.Id,
                Summary = "Stored resume snapshot.",
                SkillsJson = """["TypeScript"]""",
                ProjectHighlightsJson = """["Careeroc"]""",
                StructuredDataJson = """
                    {
                      "contact": {
                        "name": null,
                        "email": null,
                        "phone": null,
                        "location": null,
                        "links": null
                      },
                      "experience": [
                        {
                          "company": "Careeroc",
                          "title": null,
                          "location": null,
                          "dateRange": null,
                          "bullets": null
                        },
                        null
                      ],
                      "education": null,
                      "skills": {
                        "technical": null,
                        "languages": null,
                        "other": ["PostgreSQL"]
                      },
                      "projects": [
                        {
                          "name": "Resume Intelligence",
                          "description": null,
                          "tech": null,
                          "links": null,
                          "bullets": null
                        }
                      ]
                    }
                    """,
                ExtractionMetadataJson = "{}",
                ExperienceCount = 1,
                ProjectCount = 1,
                CreatedAtUtc = now
            });
        dbContext.ResumeDocuments.Add(document);
        await dbContext.SaveChangesAsync();

        var service = new ResumeIntelligenceService(dbContext, new TestClock(now));
        var workspace = await service.GetWorkspaceAsync(ApplicationDefaults.DefaultUserEmail);

        var version = Assert.Single(workspace.Documents).LatestVersion!;
        Assert.Single(version.StructuredData.Experience);
        Assert.Empty(version.StructuredData.Education);
        Assert.Single(version.StructuredData.Projects);
        Assert.Empty(version.StructuredData.Experience[0].Bullets);
        Assert.Contains("PostgreSQL", version.StructuredData.Skills.Other);
    }

    [Fact]
    public async Task PatchStructuredDataAsync_UpdatesDurableDossierAndReevaluates()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new ResumeIntelligenceService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var created = await service.CreateResumeAsync(
            new CreateResumeRequest(
                "Editable resume",
                "text",
                """
                Alex Rivera
                alex@example.com
                Education: Cascadia State Bachelor of Science Computer Science
                Experience: Software Engineer Intern 2025 built React TypeScript APIs.
                Project: Careeroc built Next.js C# .NET PostgreSQL Gmail automation.
                Skills: TypeScript, React, Next.js, C#, .NET, SQL, PostgreSQL, Docker
                """),
            ApplicationDefaults.DefaultUserEmail);
        var versionId = created.Documents.Single().LatestVersion!.Id;

        var updated = await service.PatchStructuredDataAsync(
            versionId,
            new PatchResumeStructuredDataRequest(
                [
                    new ResumeStructuredDataPatchOperation(
                        "replace",
                        "/skills/technical/0",
                        JsonSerializer.SerializeToElement("TypeScript")),
                    new ResumeStructuredDataPatchOperation(
                        "add",
                        "/projects/-",
                        JsonSerializer.SerializeToElement(
                            new
                            {
                                name = "Resume Dossier",
                                description = "Built an editable resume intelligence dossier.",
                                tech = new[] { "ASP.NET Core", "PostgreSQL" },
                                links = Array.Empty<string>(),
                                bullets = new[] { "Persisted structured resume sections as a durable JSON snapshot." }
                            }))
                ],
                "Teach the next extraction."),
            ApplicationDefaults.DefaultUserEmail);

        var document = Assert.Single(updated!.Documents);
        var version = document.LatestVersion!;
        Assert.Equal("section_patch", version.ExtractionMetadata.ExtractionMethod);
        Assert.Contains("TypeScript", version.StructuredData.Skills.Technical);
        Assert.Contains(version.StructuredData.Projects, project => project.Name == "Resume Dossier");
        Assert.Equal(2, version.Corrections.Count);
        Assert.NotEmpty(document.LatestEvaluation!.SectionReviews);
        Assert.NotNull(document.LatestEvaluation.ModelRun);
    }

    [Fact]
    public async Task ImportResumeFileAsync_CreatesResumeFromImageVisionExtraction()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var visionExtractor = new StubResumeVisionTextExtractor(
            new ResumeVisionTextExtractionDecision(
                """
                Alex Rivera
                alex@example.com
                Education: Cascadia State Bachelor of Science Computer Science
                Experience: Software Engineer Intern 2025 built React TypeScript APIs.
                Project: Careeroc built Next.js C# .NET PostgreSQL Gmail automation.
                Skills: TypeScript, React, Next.js, C#, .NET, SQL, PostgreSQL, Docker
                """,
                0.92m,
                "Test Vision Agent"));
        var service = new ResumeIntelligenceService(
            dbContext,
            NullResumeEvaluationModel.Instance,
            visionExtractor,
            new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        var response = await service.ImportResumeFileAsync(
            new ImportResumeFileRequest(
                "Screenshot",
                "resume.png",
                "image/png",
                Convert.ToBase64String([1, 2, 3])),
            ApplicationDefaults.DefaultUserEmail);

        var document = Assert.Single(response.Documents);
        Assert.Equal("Screenshot", document.Title);
        Assert.Equal("image", document.SourceKind);
        Assert.True(document.LatestEvaluation!.Score > 0);
        Assert.NotNull(visionExtractor.LastRequest);
        Assert.Equal("image", visionExtractor.LastRequest.SourceKind);
        Assert.Equal("image/png", visionExtractor.LastRequest.ContentType);
        Assert.Equal(
            dbContext.ResumeVersions.Single().EvaluationRunId,
            visionExtractor.LastModelCallContext?.RunId);
    }

    [Fact]
    public async Task ImportResumeFileAsync_RejectsImageWhenVisionExtractionUnavailable()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new ResumeIntelligenceService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ImportResumeFileAsync(
                new ImportResumeFileRequest(
                    "Screenshot",
                    "resume.png",
                    "image/png",
                    Convert.ToBase64String([1, 2, 3])),
                ApplicationDefaults.DefaultUserEmail));

        Assert.Equal("Resume image OCR/vision extraction is not configured or returned too little confidence.", exception.Message);
    }

    [Fact]
    public async Task CreateResumeAsync_AppliesModelEnhancedEvaluationWhenAvailable()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var model = new StubResumeEvaluationModel(
            new ResumeModelEvaluationDecision(
                92,
                "Model-backed review says this resume is ready for focused outreach.",
                ["Strong quantified full-stack project evidence."],
                ["Add one more internship bullet with business impact."],
                ["Tailor the top project to backend platform roles."],
                "Model-enhanced summary.",
                ["TypeScript", "React", "PostgreSQL", "Docker", "AWS", "C#", ".NET"],
                ["Careeroc agentic mailbox and resume pipeline."],
                3,
                2,
                true,
                true,
                true,
                [
                    new ResumeSectionReviewDto(
                        "experience",
                        88,
                        ["Relevant full-stack internship evidence."],
                        ["Needs sharper business impact."],
                        ["Add one quantified outcome to the strongest role."])
                ],
                0.91m,
                "Test Resume Agent"));
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName,
            TargetRole = "Software engineering intern"
        };
        dbContext.Users.Add(user);
        dbContext.JobApplications.AddRange(
            new JobApplication
            {
                User = user,
                UserId = user.Id,
                Company = "Figma",
                Role = "Software Engineering Intern",
                RoleCanonical = "Software Engineering Intern",
                Status = ApplicationStatus.Interviewing,
                LastActivityAtUtc = new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero)
            },
            new JobApplication
            {
                User = user,
                UserId = user.Id,
                Company = "Ramp",
                Role = "Backend Engineer Intern",
                RoleCanonical = "Software Engineering Intern",
                Status = ApplicationStatus.OaPending,
                LastActivityAtUtc = new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero)
            },
            new JobApplication
            {
                User = user,
                UserId = user.Id,
                Company = "Notion",
                Role = "Product Engineer Intern",
                RoleCanonical = "Product Engineer Intern",
                Status = ApplicationStatus.Rejected,
                LastActivityAtUtc = new DateTimeOffset(2026, 4, 18, 12, 0, 0, TimeSpan.Zero)
            },
            new JobApplication
            {
                User = user,
                UserId = user.Id,
                Company = "Linear",
                Role = "Product Engineer Intern",
                RoleCanonical = "Product Engineer Intern",
                Status = ApplicationStatus.Rejected,
                LastActivityAtUtc = new DateTimeOffset(2026, 4, 17, 12, 0, 0, TimeSpan.Zero)
            });
        dbContext.UserMemoryEntries.Add(
            new UserMemoryEntry
            {
                User = user,
                UserId = user.Id,
                Kind = "career",
                Key = "target_strength",
                Summary = "User wants backend platform internships and prefers evidence-first resume advice.",
                Value = "{}",
                ConfirmationStatus = "confirmed",
                UpdatedAtUtc = new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.Zero)
            });
        await dbContext.SaveChangesAsync();
        var service = new ResumeIntelligenceService(dbContext, model, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        var response = await service.CreateResumeAsync(
            new CreateResumeRequest(
                "Model resume",
                "text",
                """
                Alex Rivera
                alex@example.com
                Education: Cascadia State Bachelor of Science Computer Science
                Experience: Software Engineer Intern 2025 built React TypeScript APIs.
                Project: Careeroc built Next.js C# .NET PostgreSQL Gmail automation.
                Skills: TypeScript, React, Next.js, C#, .NET, SQL, PostgreSQL, Docker
                """),
            ApplicationDefaults.DefaultUserEmail);

        var document = Assert.Single(response.Documents);
        Assert.Equal(92, document.LatestEvaluation!.Score);
        Assert.Equal("Model-enhanced summary.", document.LatestVersion!.Summary);
        Assert.Contains("PostgreSQL", document.LatestVersion.Skills);
        Assert.Contains(document.LatestEvaluation.Strengths, strength => strength.Contains("Test Resume Agent", StringComparison.Ordinal));
        Assert.True(document.LatestEvaluation.SectionReviews.Count >= 6);
        var modelSectionReview = Assert.Single(
            document.LatestEvaluation.SectionReviews,
            review => review.Section == "experience");
        Assert.Equal("experience", modelSectionReview.Section);
        Assert.True(modelSectionReview.Score <= 88);
        Assert.Contains(modelSectionReview.Strengths, strength => strength.Contains("internship", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(document.LatestEvaluation.SectionReviews, review => review.Section == "contact");
        Assert.Contains(document.LatestEvaluation.SectionReviews, review => review.Section == "overall");
        Assert.NotNull(model.LastRequest);
        Assert.Equal("Software engineering intern", model.LastRequest.TargetRole);
        Assert.NotNull(model.LastRequest.MailboxContext);
        Assert.Contains(model.LastRequest.MailboxContext.TopTargetRoles, role => role.Contains("Software Engineering Intern", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(model.LastRequest.MailboxContext.CompaniesWithPositiveOutcomes, outcome => outcome.Contains("Figma", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(model.LastRequest.MailboxContext.RepeatedRejectedRoleFamilies, role => role.Contains("Product Engineer Intern", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(model.LastRequest.MailboxContext.LatestActionSignals, signal => signal.Contains("Ramp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(model.LastRequest.MailboxContext.ConfirmedMemoryFacts, fact => fact.Contains("backend platform", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateResumeAsync_UsesModelStructuredDataWhenAvailable()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var structuredData = new ResumeStructuredDataDto(
            new ResumeContactDto(
                "Alex Rivera",
                "demo@example.com",
                "(555) 010-0142",
                "Portland, OR",
                [
                    new ResumeLinkDto("github", "https://github.com/alexrivera"),
                    new ResumeLinkDto("linkedin", "https://linkedin.com/in/alexrivera")
                ]),
            [
                new ResumeExperienceDto(
                    "Harbor Civic Services",
                    "Software Engineer",
                    "Salem, OR",
                    "Oct 2023 - Oct 2025",
                    ["Delivered serverless workflow tools using AWS Lambda/API Gateway."]),
                new ResumeExperienceDto(
                    "Northwind",
                    "Software Engineer Intern",
                    "Irvine, CA",
                    "Jun 2022 - Sep 2022",
                    ["Built internal data quality tooling."])
            ],
            [
                new ResumeEducationDto(
                    "Lakeview Institute of Technology",
                    "M.S.",
                    "Computer Science",
                    "Sep 2026 - Jun 2028",
                    "",
                    []),
                new ResumeEducationDto(
                    "Westbrook University",
                    "B.S.",
                    "Computer Science",
                    "Sep 2021 - Jun 2025",
                    "GPA: 3.6",
                    [])
            ],
            new ResumeSkillGroupsDto(["TypeScript", "React", "C#", ".NET", "AWS"], [], []),
            [
                new ResumeProjectDto(
                    "Inbox Copilot",
                    "Designed a Gmail-first recruiting operations workspace.",
                    ["TypeScript", "Next.js", ".NET"],
                    ["https://github.com/alexrivera/careeroc-demo"],
                    ["Implemented a multi-agent orchestration pipeline."])
            ]);
        var model = new StubResumeEvaluationModel(
            new ResumeModelEvaluationDecision(
                94,
                "Model-backed structured extraction is ready.",
                ["Links and structured sections were extracted."],
                [],
                ["Tailor the resume to the target role."],
                "Model summary.",
                ["TypeScript", "React", "C#", ".NET", "AWS"],
                ["Inbox Copilot: Designed a Gmail-first recruiting operations workspace."],
                2,
                1,
                true,
                true,
                true,
                null,
                0.91m,
                "Test Resume Agent",
                structuredData));
        var service = new ResumeIntelligenceService(
            dbContext,
            model,
            new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        var response = await service.CreateResumeAsync(
            new CreateResumeRequest(
                "Model structured resume",
                "text",
                """
                Alex Rivera Portland, OR | (555) 010-0142 | demo@example.com
                WORK EXPERIENCE Harbor Civic Services Oct 2023 - Oct 2025 Software Engineer Salem, OR
                • Delivered serverless workflow tools using AWS Lambda/API Gateway.
                Northwind Jun 2022 - Sep 2022 Software Engineer Intern Irvine, CA
                • Built internal data quality tooling.
                EDUCATION Lakeview Institute of Technology M.S. Computer Science Sep 2026 - Jun 2028
                Westbrook University B.S. Computer Science Sep 2021 - Jun 2025 GPA: 3.6
                TECHNICAL PROJECTS Inbox Copilot Designed a Gmail-first recruiting operations workspace.
                SKILLS TypeScript, React, C#, .NET, AWS
                """),
            ApplicationDefaults.DefaultUserEmail);

        var version = Assert.Single(response.Documents).LatestVersion!;

        Assert.Equal("Alex Rivera", version.StructuredData.Contact.Name);
        Assert.Equal("Portland, OR", version.StructuredData.Contact.Location);
        Assert.Contains(version.StructuredData.Contact.Links, link => link.Label == "github");
        Assert.Contains(version.StructuredData.Experience, experience =>
            experience.Company == "Harbor Civic Services" &&
            experience.Title == "Software Engineer" &&
            experience.Location == "Salem, OR");
        Assert.Contains(version.StructuredData.Experience, experience =>
            experience.Company == "Northwind" &&
            experience.Title == "Software Engineer Intern" &&
            experience.Location == "Irvine, CA");
        Assert.Contains(version.StructuredData.Education, education =>
            education.School == "Lakeview Institute of Technology");
        Assert.Contains(version.StructuredData.Education, education =>
            education.School == "Westbrook University");
        Assert.Contains(version.StructuredData.Projects, project => project.Name == "Inbox Copilot");
    }

    [Fact]
    public async Task CreateResumeAsync_FallbackExtractsMultipleEducationEntries()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new ResumeIntelligenceService(
            dbContext,
            new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        var response = await service.CreateResumeAsync(
            new CreateResumeRequest(
                "Multiple education resume",
                "text",
                """
                Alex Rivera
                demo@example.com
                EDUCATION
                Lakeview Institute of Technology M.S. Computer Science Sep 2026 - Jun 2028
                Westbrook University B.S. Computer Science Sep 2021 - Jun 2025 GPA: 3.6
                WORK EXPERIENCE
                Harbor Civic Services Oct 2023 - Oct 2025 Software Engineer Salem, OR
                • Delivered serverless workflow tools using AWS Lambda/API Gateway.
                TECHNICAL PROJECTS
                Inbox Copilot
                • Designed a Gmail-first recruiting operations workspace.
                SKILLS TypeScript, React, C#, .NET, AWS, SQL
                """),
            ApplicationDefaults.DefaultUserEmail);

        var education = Assert.Single(response.Documents).LatestVersion!.StructuredData.Education;
        Assert.Contains(education, entry => entry.School == "Lakeview Institute of Technology");
        Assert.Contains(education, entry => entry.School == "Westbrook University");
    }

    [Fact]
    public async Task AddCorrectionAsync_PersistsCorrectionForSignedUserVersion()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new ResumeIntelligenceService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var created = await service.CreateResumeAsync(
            new CreateResumeRequest(
                "Correction test resume",
                "text",
                """
                Alex Rivera
                alex@example.com
                Education: Cascadia State Bachelor of Science Computer Science
                Experience: Software Engineer Intern 2025 built React TypeScript APIs.
                Project: Careeroc built Next.js C# .NET PostgreSQL Gmail automation.
                Skills: TypeScript, React, Next.js, C#, .NET, SQL, PostgreSQL, Docker
                """),
            ApplicationDefaults.DefaultUserEmail);
        var versionId = created.Documents.Single().LatestVersion!.Id;

        var updated = await service.AddCorrectionAsync(
            versionId,
            new CorrectResumeFieldRequest(
                "skills[0]",
                "typescript",
                "TypeScript",
                "Preserve display casing."),
            ApplicationDefaults.DefaultUserEmail);

        var correction = Assert.Single(updated!.Documents.Single().LatestVersion!.Corrections);
        Assert.Equal("skills[0]", correction.FieldPath);
        Assert.Equal("typescript", correction.PreviousValue);
        Assert.Equal("TypeScript", correction.CorrectedValue);
        Assert.Equal("Preserve display casing.", correction.Note);
    }

    [Fact]
    public async Task AddCorrectionAsync_DoesNotCrossUserBoundary()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new ResumeIntelligenceService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var created = await service.CreateResumeAsync(
            new CreateResumeRequest(
                "Private resume",
                "text",
                """
                Alex Rivera
                alex@example.com
                Education: Cascadia State Bachelor of Science Computer Science
                Experience: Software Engineer Intern 2025 built React TypeScript APIs.
                Project: Careeroc built Next.js C# .NET PostgreSQL Gmail automation.
                Skills: TypeScript, React, Next.js, C#, .NET, SQL, PostgreSQL, Docker
                """),
            "owner@example.com");
        var versionId = created.Documents.Single().LatestVersion!.Id;

        var updated = await service.AddCorrectionAsync(
            versionId,
            new CorrectResumeFieldRequest("summary", null, "Other user edit", null),
            "other@example.com");

        Assert.Null(updated);
    }

    [Fact]
    public async Task AddCorrectionAsync_RecomputesVersionAndEvaluationFromCorrections()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new ResumeIntelligenceService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var created = await service.CreateResumeAsync(
            new CreateResumeRequest(
                "Sparse resume",
                "text",
                """
                Alex Rivera
                Built campus automation tools and backend services for student workflows.
                Project: Careeroc built a data pipeline and dashboard for job applications.
                Skills: React, SQL
                Portfolio available on request.
                """),
            ApplicationDefaults.DefaultUserEmail);
        var versionId = created.Documents.Single().LatestVersion!.Id;
        var originalScore = created.Documents.Single().LatestEvaluation!.Score;

        await service.AddCorrectionAsync(
            versionId,
            new CorrectResumeFieldRequest(
                "skills",
                null,
                "TypeScript, React, Next.js, C#, .NET, SQL, PostgreSQL, Docker",
                "The parser missed the full skills line."),
            ApplicationDefaults.DefaultUserEmail);
        await service.AddCorrectionAsync(
            versionId,
            new CorrectResumeFieldRequest("contact", null, "present", null),
            ApplicationDefaults.DefaultUserEmail);
        var updated = await service.AddCorrectionAsync(
            versionId,
            new CorrectResumeFieldRequest("education", null, "present", null),
            ApplicationDefaults.DefaultUserEmail);

        var document = updated!.Documents.Single();
        Assert.True(document.LatestVersion!.Skills.Count >= 8);
        Assert.True(document.LatestEvaluation!.Evidence.HasContact);
        Assert.True(document.LatestEvaluation.Evidence.HasEducation);
        Assert.True(document.LatestEvaluation.Score > originalScore);
        var memory = Assert.Single(dbContext.UserMemoryEntries.ToList());
        Assert.Equal("resume.latest_evaluation", memory.Key);
        Assert.Contains($"{document.LatestEvaluation.Score}/100", memory.Summary, StringComparison.Ordinal);
    }

    private sealed class StubResumeEvaluationModel(
        ResumeModelEvaluationDecision? decision) : IResumeEvaluationModel
    {
        public ResumeModelEvaluationRequest? LastRequest { get; private set; }

        public Task<ResumeModelEvaluationDecision?> EvaluateAsync(
            ResumeModelEvaluationRequest request,
            CancellationToken cancellationToken = default,
            ModelCallContext? modelCallContext = null)
        {
            LastRequest = request;
            return Task.FromResult(decision);
        }
    }

    private sealed class StubResumeVisionTextExtractor(
        ResumeVisionTextExtractionDecision? decision) : IResumeVisionTextExtractor
    {
        public ResumeVisionTextExtractionRequest? LastRequest { get; private set; }
        public ModelCallContext? LastModelCallContext { get; private set; }

        public Task<ResumeVisionTextExtractionDecision?> ExtractAsync(
            ResumeVisionTextExtractionRequest request,
            CancellationToken cancellationToken = default,
            ModelCallContext? modelCallContext = null)
        {
            LastRequest = request;
            LastModelCallContext = modelCallContext;
            return Task.FromResult(decision);
        }
    }
}
