using JWT.Algorithms;
using JWT.Builder;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CRM.Flexie.Fiskalizimi
{
    public class Fiskalizimi
    {
        protected Invoice? Invoice { get; set; }

        protected string Key { get; set; }

        public Fiskalizimi(string key)
        {
            Key = key;
        }

        public Invoice NewInvoice(Invoice invoice, string method = "sync")
        {
            Invoice = invoice;
            NewInvoiceToFlexieAsync().Wait();

            return Invoice;
        }

        protected async Task NewInvoiceToFlexieAsync()
        {
            if (Invoice != null && Invoice?.GetType().GetProperties().Length == 0)
            {
                throw new Exception("Invoice object not initialized. Create Invoice object first, then send to Flexie");
            }

            try
            {
                var res = await SendPayload(Endpoint.FX_NEW_INVOICE, Invoice.ToJSON());

                if (res.IsSuccessStatusCode)
                {
                    string result = await res.Content.ReadAsStringAsync();
                    Dictionary<string, object> responseData = JsonConvert.DeserializeObject<Dictionary<string, object>>(result);
                    
                    if (responseData["ok"] == null || (Boolean) responseData["ok"] == false)
                    {
                        throw new Exception("There was an error at Fiskalizimi. Error Code " + responseData["fz_error_code"] + ". Error Message " + responseData["fz_error_message"]);
                    }

                    // Enrich invoice with data coming from Flexie CRM
                    Invoice.EnrichInvoice(responseData);
                }
                else
                {
                    throw new Exception("There was an error on HTTP request, failed with code " + res.StatusCode.ToString());
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public async Task<string> GetEInvoiceCodeAsync(string nivf)
        {
            try
            {
                var res = await SendPayload(
                    Endpoint.FX_GET_EIC,
                        JsonConvert.SerializeObject(new Dictionary<string, string>
                            {
                                { "nivf", nivf }
                            }
                        )
                    );

                if (res.IsSuccessStatusCode)
                {
                    string result = await res.Content.ReadAsStringAsync();
                    Dictionary<string, object> responseData = JsonConvert.DeserializeObject<Dictionary<string, object>>(result);

                    if (responseData["ok"] == null || (Boolean) responseData["ok"] == false)
                    {
                        throw new Exception("EIC not found, there have been probably an issue while sending e-invoice in Fiskalizimi service. Flexie has a retry mechanis, so best thing to do is to retry this method later on.");
                    }

                    return (string) responseData["eic"];
                } else
                {
                    throw new Exception("There was an error on HTTP request, failed with code " + res.StatusCode.ToString());
                }
            }
            catch(Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        protected async Task<HttpResponseMessage> SendPayload(Dictionary<string, string> endpoint, string? payload)
        {
            HttpClient httpClient = new HttpClient();

            // Generate token for Flexie Dynamic Endpoint
            string token = JwtBuilder.Create()
                      .WithAlgorithm(new HMACSHA256Algorithm())
                      .WithSecret(endpoint["secret"])
                      .AddClaim("iss", endpoint["key"])
                      .AddClaim("exp", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds())
                      .AddClaim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                      .Encode();

            // Add credentials in the header
            httpClient.DefaultRequestHeaders.Add("token", token);
            httpClient.DefaultRequestHeaders.Add("key", Key);


            StringContent data = new StringContent(payload, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(endpoint["url"], data).ConfigureAwait(false);

            return response;
        }
    }
}
