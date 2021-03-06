﻿using arq.Common.Utilities;
using arq.Common.VO;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using TimeTrackerTfs.Model;

namespace TimeTrackerTfs.BO
{
    public class WorkItemBO
    {
        public static string TfsUrl;
        private string tfsUrlApi
        {
            get
            {
                return TfsUrl + "_apis/wit/";
            }
        } 
        private string workitemIds
        {
            get
            {
                return tfsUrlApi + "workitems?ids={0}&api-version=1.0";
            }
        }
        private string workItemQuery
        {
            get
            {
                return tfsUrlApi + "wiql?api-version=1.0";
            }
        }
        //private string workItemGetSpecific = "workitems/{0}";

        private QueryDTO queryInProgressTodo
        {
            get
            {
                return new QueryDTO
                {
                    query = "Select [System.Id]" +
                " FROM WorkItems WHERE"
                + " [System.State] IN ('In Progress','To Do') AND [System.AssignedTo] = @Me"
                };
            }
        }
        private QueryDTO queryValidInProgress
        {
            get
            {
                return new QueryDTO
                {
                    query = "Select [System.Id]" +
                " FROM WorkItems WHERE"
                + " [System.State] IN ('In Progress') AND Microsoft.VSTS.CMMI.Blocked <> 'Yes' AND [System.AssignedTo] = @Me"
                };
            }
        }
        private List<WorkItemDTO> processQuery(QueryDTO query)
        { 
            List<WorkItemDTO> lstWork = new List<WorkItemDTO>();
            var result = RequestUtil.Post(workItemQuery, JsonConvert.SerializeObject(query), null, CredentialCache.DefaultCredentials);
            WorkItemQueryResult qRes = JsonConvert.DeserializeObject<WorkItemQueryResult>(result);
            IEnumerable<WorkItemReference> workItemRefs;
            int skip = 0, qtd = 5;
            do
            {
                workItemRefs = qRes.WorkItems.Skip(skip).Take(qtd);
                if (workItemRefs.Any())
                {
                    string url = string.Format(workitemIds, string.Join(",", workItemRefs.Select(t => t.Id).ToList()));
                    var resultW = RequestUtil.Get(url, null, CredentialCache.DefaultCredentials);
                    var lstAux = JsonConvert.DeserializeObject<TfsDefault<WorkItem>>(resultW);
                    lstAux.value.ForEach(t => lstWork.Add(new WorkItemDTO(t)));
                }
                skip += qtd;
            }
            while (workItemRefs.Count() == qtd);
            return lstWork;
        }
        private WorkItem UpdateWorkItem(int id, JsonPatchDocument j)
        {
            VssCredentials creds = new VssCredentials(true);
            VssConnection connection = new VssConnection(new Uri(TfsUrl), creds);
            WorkItemTrackingHttpClient witClient = connection.GetClient<WorkItemTrackingHttpClient>();
            return witClient.UpdateWorkItemAsync(j, id).Result;
        }

        public BusinessObject<WorkItemDTO> GetValidInProgress()
        {
            try
            {
                if (string.IsNullOrEmpty(TfsUrl))
                    throw new Exception("Tfs not defined.");
                return processQuery(queryValidInProgress).ToBusinessObject();
            }
            catch(Exception ex)
            {
                return ConstructError<WorkItemDTO>.Set(ex);
            }
        }
        public BusinessObject<WorkItemDTO> GetToDoInProgress()
        {
            try
            {
                if (string.IsNullOrEmpty(TfsUrl))
                    throw new Exception("Tfs not defined.");
                string cacheName = "_GetToDoInProgress";
                List<WorkItemDTO> lstWork = CacheUtil.RecuperarCacheObjSemReferencia<List<WorkItemDTO>>(cacheName);
                if (lstWork != null)
                    return lstWork.ToBusinessObject();
                lstWork = processQuery(queryInProgressTodo);
                return lstWork.ToBusinessObject();
            }
            catch(Exception ex)
            {
                return ConstructError<WorkItemDTO>.Set(ex);
            }
        }

        public BusinessObject<WorkItemDTO> Play(WorkItemDTO wkPlayed, WorkItemDTO old = null)
        {
            try
            {
                if (string.IsNullOrEmpty(TfsUrl))
                    throw new Exception("Tfs not defined.");
                if (old != null)
                {
                    old.Blocked = "Yes";
                    JsonPatchDocument j = new JsonPatchDocument();
                    j.Add(new JsonPatchOperation
                    {
                        Operation = Operation.Add,
                        Path = "/fields/Microsoft.VSTS.CMMI.Blocked",
                        Value = "Yes"
                    });
                    UpdateWorkItem(old.Id, j);
                }
                JsonPatchDocument jnew = new JsonPatchDocument();
                jnew.Add(new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/Microsoft.VSTS.CMMI.Blocked",
                    Value = ""
                });
                jnew.Add(new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.State",
                    Value = "In Progress"
                });
                return new WorkItemDTO(UpdateWorkItem(wkPlayed.Id, jnew)).EntityToBusinessObject();
            }
            catch(Exception ex)
            {
                return ConstructError<WorkItemDTO>.Set(ex);
            }
        }

        private object processDouble(double value)
        {
            double dbl = Math.Round(value, 4);
            string str = dbl.ToString();
            if (str.Contains(","))
                return dbl.ToString();
            return dbl;
        }

        public BusinessObject<WorkItemDTO> UpdateWorked(WorkItemDTO wik)
        {
            try
            {
                if (string.IsNullOrEmpty(TfsUrl))
                    throw new Exception("Tfs not defined.");
                JsonPatchDocument j = new JsonPatchDocument();
                j.Add(new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/Microsoft.VSTS.Scheduling.CompletedWork",
                    Value = processDouble(wik.CompletedWork)
                });
                j.Add(new JsonPatchOperation
                {
                    Operation = wik.RemainingWork == 0 ? Operation.Add : Operation.Replace,
                    Path = "/fields/Microsoft.VSTS.Scheduling.RemainingWork",
                    Value = processDouble(wik.RemainingCalc)
                });
                if (wik.OriginalEstimate == 0)
                {
                    j.Add(new JsonPatchOperation
                    {
                        Operation = Operation.Add,
                        Path = "/fields/Microsoft.VSTS.Scheduling.OriginalEstimate",
                        Value = processDouble(wik.RemainingWork)
                    });
                }
                var ret = UpdateWorkItem(wik.Id, j);
                return new WorkItemDTO(ret).EntityToBusinessObject();
            }
            catch(Exception ex)
            {
                return ConstructError<WorkItemDTO>.Set(ex);
            }
        }
    }
}
