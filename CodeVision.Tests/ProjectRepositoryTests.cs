﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using CodeVision.Dependencies;
using CodeVision.Dependencies.Nugets;
using Dapper;
using NUnit.Framework;
using Package = CodeVision.Dependencies.Nugets.Package;
using Project = CodeVision.Dependencies.Nugets.Project;

namespace CodeVision.Tests
{
    [TestFixture]
    public class ProjectRepositoryTests
    {
        private SqlConnection _connection;
        private string _connectionString;

        [TestFixtureSetUp]
        public void Setup()
        {
            var configuration = CodeVisionConfigurationSection.Load();
            _connectionString = configuration.DependencyGraphConnectionString;
            _connection = new SqlConnection(_connectionString);
        }

        [TestFixtureTearDown]
        public void TearDown()
        {
            _connection.Close();
        }

        [Test]
        public void ProjectRepository_CanSaveProject()
        {
            CleanUpDatabase();
            var project = new Project
            {
                Name = "Console App",
                OutputKind = "Console",
                Platform = "Any",
                Packages = new List<Package>
                {
                    new Package { Name = "Nuget1", TargetFramework = "4.5", Version  = "1.0" },
                    new Package { Name = "Nuget2", TargetFramework = "4.5", Version  = "1.0" }
                }
            };

            using (var repository = new ProjectRepository(_connectionString))
            {
                repository.SaveProject(project);
            }
            
            Assert.That(GetTableRowCount("Project"), Is.EqualTo(1));
            Assert.That(GetTableRowCount("Package"), Is.EqualTo(2));
            Assert.That(GetTableRowCount("ProjectPackage"), Is.EqualTo(2));

            var anotherProjectThatUsesTheSameNuget = new Project
            {
                Name = "Some DLL",
                OutputKind = "Dll",
                Platform = "x86",
                Packages = new List<Package>
                {
                    new Package { Name = "Nuget1", TargetFramework = "4.5", Version  = "1.0" },
                }
            };

            using (var repository = new ProjectRepository(_connectionString))
            {
                repository.SaveProject(anotherProjectThatUsesTheSameNuget);
            }

            Assert.That(GetTableRowCount("Project"), Is.EqualTo(2));
            Assert.That(GetTableRowCount("Package"), Is.EqualTo(2));
            Assert.That(GetTableRowCount("ProjectPackage"), Is.EqualTo(3));

            // Meaning Nuget1 and Nuget2 are no longer part of this project
            var sameProjectNewNuget = new Project
            {
                Name = "Console App",
                OutputKind = "Console",
                Platform = "Any",
                Packages = new List<Package>
                {
                    new Package { Name = "Nuget3", TargetFramework = "4.6.1", Version  = "1.1" },
                }
            };

            using (var repository = new ProjectRepository(_connectionString))
            {
                repository.SaveProject(sameProjectNewNuget);
            }

            Assert.That(GetTableRowCount("Project"), Is.EqualTo(2));
            Assert.That(GetTableRowCount("Package"), Is.EqualTo(3));
            Assert.That(GetTableRowCount("ProjectPackage"), Is.EqualTo(2)); // SomeDll has one and Console App has one

            var duplicateProject = new Project
            {
                Name = "Console App",
                OutputKind = "Console",
                Platform = "Any",
                Packages = new List<Package>
                {
                    new Package { Name = "Nuget3", TargetFramework = "4.6.1", Version  = "1.1" },
                }
            };

            using (var repository = new ProjectRepository(_connectionString))
            {
                repository.SaveProject(duplicateProject);
            }

            Assert.That(GetTableRowCount("Project"), Is.EqualTo(2));
            Assert.That(GetTableRowCount("Package"), Is.EqualTo(3));
            Assert.That(GetTableRowCount("ProjectPackage"), Is.EqualTo(2)); // just like one above
        }

        [Test]
        public void ProjectRepository_CanSearch()
        {
            CleanUpDatabase();
            using (var repository = new ProjectRepository(_connectionString))
            {
                repository.SaveProject(
                    new Project
                    {
                        Name = "Console App",
                        Packages = new List<Package>
                        {
                            new Package {Name = "Console"},
                            new Package {Name = "Console Test"},
                            new Package {Name = "Foo"},
                            new Package {Name = "Foo Test"},
                        }
                    });
                Assert.That(repository.GetPackages("Console").Count, Is.EqualTo(2));
            }
        }

        [Test]
        public void ProjectRepository_DependencyMatrix()
        {
            CleanUpDatabase();
            var project = new Project
            {
                Name = "Console App",
                OutputKind = "Console",
                Platform = "Any",
                Packages = new List<Package>
                {
                    new Package { Name = "Nuget1", TargetFramework = "4.5", Version  = "1.0" },
                    new Package { Name = "Nuget2", TargetFramework = "4.5", Version  = "1.0" },
                    new Package { Name = "Nuget3", TargetFramework = "4.6.1", Version  = "1.1" },
                }
            };

            using (var repository = new ProjectRepository(_connectionString))
            {
                repository.SaveProject(project);
            }

            var anotherProjectThatUsesTheSameNuget = new Project
            {
                Name = "Some DLL",
                OutputKind = "Dll",
                Platform = "x86",
                Packages = new List<Package>
                {
                    new Package { Name = "Nuget1", TargetFramework = "4.5", Version  = "1.0" },
                }
            };

            using (var repository = new ProjectRepository(_connectionString))
            {
                repository.SaveProject(anotherProjectThatUsesTheSameNuget);
            }
            

            DependencyMatrix dm;
            using (var repository = new ProjectRepository(_connectionString))
            {
                //           ConsoleApp SomeDll
                // Nuget1        x         x
                // Nuget2        x
                // Nuget3        x  
                dm = repository.GetDependencyMatrix();
            }

            CollectionAssert.AreEqual(dm.Rows.Select(s => s.Value), new string[] { "Nuget1 1.0", "Nuget2 1.0", "Nuget3 1.1" });
            CollectionAssert.AreEqual(dm.Columns.Select(s => s.Value), new string[] { "Console App", "Some DLL" });
            Assert.IsTrue(dm.Matrix[dm.Rows[0].Id, dm.Columns[0].Id]);
            Assert.IsTrue(dm.Matrix[dm.Rows[0].Id, dm.Columns[1].Id]);
            Assert.IsTrue(dm.Matrix[dm.Rows[1].Id, dm.Columns[0].Id]);
        }

        [Test]
        public void ProjectRepository_NuGetRemoved()
        {
            CleanUpDatabase();

            // Suppose we have project with two nugets
            var project = new Project
            {
                Name = "Console App",
                OutputKind = "Console",
                Platform = "Any",
                Packages = new List<Package>
                {
                    new Package { Name = "Nuget1", TargetFramework = "4.5", Version  = "1.0" },
                    new Package { Name = "Nuget2", TargetFramework = "4.5", Version  = "1.0" }
                }
            };

            // We save it
            using (var repository = new ProjectRepository(_connectionString))
            {
                repository.SaveProject(project);
            }

            Assert.That(GetTableRowCount("Project"), Is.EqualTo(1));
            Assert.That(GetTableRowCount("Package"), Is.EqualTo(2));
            Assert.That(GetTableRowCount("ProjectPackage"), Is.EqualTo(2));

            // .. and than Nuget2 gets removed from this project and we load this project with just one NuGet:
            var sameProjectOneNugetLess = new Project
            {
                Name = "Console App",
                OutputKind = "Console",
                Platform = "Any",
                Packages = new List<Package>
                {
                    new Package { Name = "Nuget1", TargetFramework = "4.5", Version  = "1.0" }
                }
            };

            using (var repository = new ProjectRepository(_connectionString))
            {
                repository.SaveProject(sameProjectOneNugetLess);
            }

            // Now there should be only one nuget
            Assert.That(GetTableRowCount("Project"), Is.EqualTo(1));
            Assert.That(GetTableRowCount("Package"), Is.EqualTo(2)); // There are still two packages
            Assert.That(GetTableRowCount("ProjectPackage"), Is.EqualTo(1)); // but only one package left in this project
        }


        [Test]
        public void ProjectRepository_MultipleNugetVersions()
        {
            CleanUpDatabase();

            // Suppose we have projects with with the same nuget name, but different versions
            var project1 = new Project
            {
                Name = "Console App",
                OutputKind = "Console",
                Platform = "Any",
                Packages = new List<Package>
                {
                    new Package { Name = "Nuget1", TargetFramework = "4.5", Version  = "1.0" },
                }
            };

            var project2 = new Project
            {
                Name = "Console App2",
                OutputKind = "Console",
                Platform = "Any",
                Packages = new List<Package>
                {
                    new Package { Name = "Nuget1", TargetFramework = "4.5", Version  = "2.0" },
                }
            };

            // We save both of them
            using (var repository = new ProjectRepository(_connectionString))
            {
                repository.SaveProject(project1);
                repository.SaveProject(project2);
            }

            Assert.That(GetTableRowCount("Project"), Is.EqualTo(2));
            Assert.That(GetTableRowCount("Package"), Is.EqualTo(2)); // That's the whole point
            Assert.That(GetTableRowCount("ProjectPackage"), Is.EqualTo(2));
        }

        protected int GetTableRowCount(string tableName)
        {
            return Convert.ToInt32(_connection.ExecuteScalar($"select count(*) from {tableName};"));
        }

        private void CleanUpDatabase()
        {
            _connection.Execute("delete from ProjectPackage;");
            _connection.Execute("delete from Project;");
            _connection.Execute("delete from Package;");
        }
    }
}