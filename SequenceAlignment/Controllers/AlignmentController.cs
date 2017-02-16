﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SequenceAlignment.ViewModels;
using Newtonsoft.Json;
using SequenceAlignment.Services;
using Microsoft.AspNetCore.Http;
using SequenceAlignment.Models;
using System.Security.Claims;
using BioEdge.MatricesHelper;
using BioEdge.Matrices;
using BioEdge.Alignment;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNetCore.Authorization;

namespace SequenceAlignment.Controllers
{
    public class AlignmentController : Controller
    {
        private readonly SequenceAlignmentDbContext db;
        public AlignmentController(SequenceAlignmentDbContext _db)
        {
            db = _db;
        }

        [AllowAnonymous]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Align()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Align(SequenceViewModel Model, IFormFile FirstFile , IFormFile SecondFile)
        {
            if (string.IsNullOrWhiteSpace(Model.FirstSequence))
            {
                string FirstSequence = await Helper.ConvertFileByteToByteStringAsync(FirstFile);
                if (FirstSequence.Length > 20000)
                    return RedirectToAction("Grid", "Alingmnet");
                else
                    Model.FirstSequence = FirstSequence;
            }
            if (string.IsNullOrWhiteSpace(Model.SecondSequence))
            {
                string SecondSequence = await Helper.ConvertFileByteToByteStringAsync(FirstFile);
                if (SecondSequence.Length > 20000)
                    return RedirectToAction("Grid", "Alingmnet");
                else
                    Model.SecondSequence = SecondSequence;
            }
            Sequence SeqFound = Helper.AreFound(db.Sequences,Helper.SHA1HashStringForUTF8String(Model.FirstSequence),Helper.SHA1HashStringForUTF8String(Model.SecondSequence));
            if (SeqFound == null)
            {
                SeqFound = new Sequence();
                SeqFound.AlignmentID = Guid.NewGuid().ToString();
                SeqFound.FirstSequence = Model.FirstSequence;
                SeqFound.FirstSequenceHash = Helper.SHA1HashStringForUTF8String(SeqFound.FirstSequence);
                SeqFound.SecondSequence= Model.SecondSequence;
                SeqFound.SecondSequenceHash = Helper.SHA1HashStringForUTF8String(SeqFound.SecondSequence);
                SequenceAligner AlgorithmInstance = DynamicInvoke.GetAlgorithm(Model.Algorithm);
                ScoringMatrix ScoringMatrixInstance = DynamicInvoke.GetScoreMatrix(Model.ScoringMatrix);
                string AlignmentResult = string.Empty;
                float AlignmentScore = 0.0f;
                await Task.Run(() =>
                {
                    AlignedSequences Result = AlgorithmInstance.Align(Model.FirstSequence, Model.SecondSequence, ScoringMatrixInstance, Model.Gap);
                    AlignmentResult = Result.StandardFormat(210);
                    AlignmentScore = Result.AlignmentScore(ScoringMatrixInstance);
                });
                SeqFound.ByteText = Helper.GetText(AlignmentResult, 
                                                    AlignmentScore,
                                                    SeqFound.AlignmentID, 
                                                    Model.Algorithm,
                                                    Model.ScoringMatrix,
                                                    Model.Gap, 
                                                    Model.GapOpenPenalty,
                                                    Model.GapExtensionPenalty);
                SeqFound.UserFK = User.FindFirstValue(ClaimTypes.NameIdentifier);
                await db.AddAsync(SeqFound);
                await db.SaveChangesAsync();
                return File(SeqFound.ByteText, "plain/text", $"{SeqFound.AlignmentID}_Alignment_Result.txt");
            }
            else
            {
                return File(SeqFound.ByteText, "plain/text", $"{SeqFound.AlignmentID}_Alignment_Result.txt");
            }
        }

        [HttpGet]
        public IActionResult Grid()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Grid(GridViewModel Model, IFormFile FirstSequenceFile, IFormFile SecondSequenceFile)
        {
            string FirstSequence = await Helper.ConvertFileByteToByteStringAsync(FirstSequenceFile);
            string SecondSequence = await Helper.ConvertFileByteToByteStringAsync(SecondSequenceFile);
            if (FirstSequence.Length <= 20000 || SecondSequence.Length <= 20000)
               return RedirectToAction("Align", "Alignment");
            // Check for earlier exist
            Sequence SeqFound = Helper.AreFound(db.Sequences, Helper.SHA1HashStringForUTF8String(FirstSequence), Helper.SHA1HashStringForUTF8String(SecondSequence));
            if (SeqFound == null) // Means the user didn't not submit these sequences before.
            {
                string AlignmentID = Guid.NewGuid().ToString();
                // Storing in the database
                await db.AddAsync(new Sequence {
                                                 AlignmentID = AlignmentID,
                                                 FirstSequence = FirstSequence,
                                                 FirstSequenceHash = Helper.SHA1HashStringForUTF8String(SeqFound.FirstSequence),
                                                 SecondSequence = SecondSequence,
                                                 SecondSequenceHash = Helper.SHA1HashStringForUTF8String(SeqFound.SecondSequence),
                                                 UserFK = User.FindFirstValue(ClaimTypes.NameIdentifier) });
                await db.SaveChangesAsync();
                // Sending to the Grid, that there is a job is required from you
                var connection = new HubConnection(@"http://mtidna.azurewebsites.net"); // Setting the URL of the SignalR server
                var _hub = connection.CreateHubProxy("GridHub"); // Setting the Hub Communication
                await connection.Start(); // Start the connection 
                await _hub.Invoke("Alignment", AlignmentID); // Invoke Alignment SignalR Method, and pass the Job Id to the Grid.
                return View("Notify", AlignmentID);
            }
            else
            {
                if(SeqFound.ByteText == null) // a failure happened before sending all the data to the Grid or the user re-submitted the same two sequences before finishing 
                {
                    return View("Notify", SeqFound.AlignmentID); // Returning the same Alignment ID
                }
                else // the grid already alignment them , so no action is required from the grid, the user can download a text file directly.
                {
                    return File(SeqFound.ByteText, "plain/text", $"{SeqFound.AlignmentID}_Clould_Result.txt");
                }
            }
        }

    }
}

