﻿using System.Collections.Generic;
using System.Web.Http;

using AutoMapper;
using CodeVision.Dependencies;

namespace CodeVision.Web.Controllers.Api
{
    public class DependencyGraphController : ApiController
    {
        private readonly DependencyGraph _graph;        

        public DependencyGraphController(DependencyGraph graph)
        {
            _graph = graph;
        }
        
        [Route("api/modules")]
        public IList<ViewModels.Module> Get (string name)
        {
            var items = _graph.GetModulesBeginsWith(name);
            return Mapper.Map<List<ViewModels.Module>>(items);
        }

        [Route("api/graph/{moduleId}/{direction}/{level}")]
        public IList<ViewModels.Module> Get(int moduleId, DependencyDirection direction, DependencyLevel level)
        {
            var items = _graph.GetDependencies(moduleId, direction, level);
            return Mapper.Map<List<ViewModels.Module>>(items);
        }
    }
}
