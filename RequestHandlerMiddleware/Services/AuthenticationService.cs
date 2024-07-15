using Amazon.Runtime.Internal.Util;
using AutoMapper;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using AuthenticationProtos;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace RequestHandlerMiddleware.Services
{
    public class AuthenticationService : Authentication.AuthenticationBase
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        private static readonly string adminUsername = "admin";
        private static readonly string adminPassword = "pwd";
        private string ServerKey;

        public AuthenticationService(IConfiguration configuration)
        {
            ServerKey = (string)(configuration.GetValue(typeof(string), "ServerKey") ?? "TWlkZGxld2FyZUtleQ==TWlkZGxld2FyZUtleQ==TWlkZGxld2FyZUtleQ==");
        }

        public override Task<UserCredsAnswer> Authenticate(UserCreds request, ServerCallContext context)
        {
            var result = new UserCredsAnswer()
            {
                Token = "-1"
            };

            try
            {

                if(request.Username.Equals(adminUsername) && request.Password.Equals(adminPassword))
                {
                    var tokenHandler = new JwtSecurityTokenHandler();

                    //get token key
                    var tokenKey = Encoding.ASCII.GetBytes(ServerKey);

                    //user claims
                    var claims = new List<Claim>()
                    {
                        new Claim(ClaimTypes.Role, "Admin"),
                        new Claim(ClaimTypes.Name, request.Username)
                    };


                    //token descriptor with claims
                    var tokenDescriptor = new SecurityTokenDescriptor()
                    {
                        Subject = new ClaimsIdentity(claims),
                        Expires = null,
                        SigningCredentials = new SigningCredentials(
                            new SymmetricSecurityKey(tokenKey), SecurityAlgorithms.HmacSha256Signature)
                    };

                    //token creation
                    var token = tokenHandler.CreateToken(tokenDescriptor);

                    if (token == null)
                        return Task.FromResult(result);

                    result.Token = tokenHandler.WriteToken(token);
                }
                else
                {
                    log.Warn("Invalid authentication attempt!");
                }
            }
            catch (Exception ex)
            {
                log.Error(ex + "In Authenticate()!");
            }

            return Task.FromResult(result);
        }
    }
}