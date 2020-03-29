using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bogus;
using Bogus.Extensions;
using DynamicData;
using DynamicData.Kernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SpaServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VueCliMiddleware;

namespace dd_vue_signalr
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddRazorPages();
            services.AddSignalR()
                .AddJsonProtocol(x =>
                {
                    x.PayloadSerializerOptions.Converters.Add(new OptionalConverterFactory());
                    x.PayloadSerializerOptions.Converters.Add(new ChangeReasonConverter());
                });
            // NOTE: PRODUCTION Ensure this is the same path that is specified in your webpack output
            services.AddSpaStaticFiles(opt => opt.RootPath = "wwwroot");

            services.AddMvcCore()
                .AddJsonOptions(z =>
                {
                    z.JsonSerializerOptions.Converters.Add(new OptionalConverterFactory());
                    z.JsonSerializerOptions.Converters.Add(new ChangeReasonConverter());
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // NOTE: PRODUCTION uses webpack static files
            app.UseSpaStaticFiles();

            app.UseCors(builder =>
            {
                builder
                    .SetIsOriginAllowed(x => true)
                    .AllowAnyHeader()
                    .WithMethods("GET", "POST")
                    .AllowCredentials();
            });

            app.UseRouting();

            app.Map("/endpoints", builder => builder.Run(async context =>
            {
                var data = typeof(RouteOptions)
                            .GetProperty("EndpointDataSources", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .GetValue(context.RequestServices.GetService<IOptions<RouteOptions>>().Value) as
                    IEnumerable<EndpointDataSource>;
                var allEndpoints = data.SelectMany(z => z.Endpoints);
                foreach (var end in allEndpoints)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(end.DisplayName);
                    sb.AppendLine(string.Join(", ",
                        end.Metadata.OfType<HubMetadata>().Select(z => z.HubType.FullName)));
                    await context.Response.WriteAsync(sb.ToString());
                }
            }));

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<DataHub>("/data");

// #if DEBUG
//                 // NOTE: VueCliProxy is meant for developement and hot module reload
//                 // NOTE: SSR has not been tested
//                 // Production systems should only need the UseSpaStaticFiles() (above)
//                 // You could wrap this proxy in either
//                 // if (System.Diagnostics.Debugger.IsAttached)
//                 // or a preprocessor such as #if DEBUG
//                 endpoints.MapToVueCliProxy(
//                     "{*path}",
//                     new SpaOptions {SourcePath = "."},
//                     npmScript: "serve",
//                     regex: "App running at",
//                     forceKill: true
//                 );
// #endif
            });
        }
    }

    class ChangeReasonConverter : JsonConverterFactory
    {
        private readonly JsonStringEnumConverter _converter;

        public ChangeReasonConverter()
        {
            _converter = new JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(ChangeReason) == objectType;
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            return _converter.CreateConverter(typeToConvert, options);
        }
    }

    class OptionalConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsGenericType &&
                   typeof(Optional<>).IsAssignableFrom(typeToConvert.GetGenericTypeDefinition());
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            return Activator.CreateInstance(
                typeof(OptionalConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0])) as JsonConverter;
        }
    }

    class OptionalConverter<T> : JsonConverter<Optional<T>>
    {
        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                JsonSerializer.Serialize(writer, value.Value, options);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    public class Person
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public int Seed { get; set; }
        public string Name => $"{FirstName} {LastName}";
        public Guid? ParentId { get; set; }
    }

    public class PersonWithChildren : Person, IDisposable
    {
        private IEnumerable<Person> _children = Enumerable.Empty<Person>();
        private IDisposable _disposable;

        public PersonWithChildren(Node<Person, Guid> node)
        {
            Id = node.Item.Id;
            FirstName = node.Item.FirstName;
            LastName = node.Item.LastName;
            ParentId = node.Item.ParentId;
            Seed = node.Item.Seed;
            _disposable = node.Children.Connect()
                .Transform(z => z.Item)
                .ToCollection()
                .Do(x => Children = x)
                .Subscribe();
        }

        public IEnumerable<Person> Children
        {
            get => _children;
            set => _children = value;
        }

        public void Dispose()
        {
            _disposable.Dispose();
        }
    }

    public class PersonData : IDisposable
    {
        private readonly CompositeDisposable _disposable = new CompositeDisposable();
        private SourceCache<Person, Guid> _cache;

        public static PersonData Instance = new PersonData();

        public PersonData()
        {
            _cache = new SourceCache<Person, Guid>(x => x.Id);
            _disposable.Add(
                _cache.ExpireAfter(x => TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)).Subscribe()
            );
            var personFaker = new Faker<Person>()
                    .RuleFor(z => z.Id, f => f.Random.Guid())
                    .RuleFor(z => z.FirstName, f => f.Name.FirstName())
                    .RuleFor(z => z.LastName, f => f.Name.LastName())
                    .RuleFor(x => x.ParentId, f => f.PickRandom(_cache.Items.OrderByDescending(z => z.Seed).Take(5)).Id)
                ;

            const int parents = 6;

            _cache.AddOrUpdate(Enumerable.Range(0, parents)
                .Select(z =>
                {
                    var item = personFaker.Clone().RuleFor(z => z.ParentId, f => null).UseSeed(z).Generate();
                    item.Seed = z;
                    return item;
                })
            );

            _disposable.Add(
                    Observable.Interval(TimeSpan.FromSeconds(2))
                        .Select(z =>
                        {
                            var seed = Convert.ToInt32(z) + parents;
                            var item = personFaker.UseSeed(seed).Generate();
                            item.Seed = seed;
                            return item;
                        })
                        .Subscribe(person => _cache.AddOrUpdate(person)))
                ;
        }

        public IObservable<IChangeSet<Person, Guid>> GetPeople()
        {
            return _cache.AsObservableCache().Connect();
        }

        public void Dispose()
        {
            _disposable?.Dispose();
        }
    }

    public class DataHub : Hub
    {
        public ChannelReader<IChangeSet<Person, Guid>> People(CancellationToken cancellationToken)
        {
            return PersonData.Instance.GetPeople().ConnectToChannel(cancellationToken, TaskPoolScheduler.Default);
        }

        public ChannelReader<IChangeSet<PersonWithChildren, Guid>> PeopleTree(CancellationToken cancellationToken)
        {
            return PersonData.Instance.GetPeople()
                .TransformToTree(x => x.ParentId ?? Guid.Empty)
                .Transform(z => new PersonWithChildren(z))
                .ConnectToChannel(cancellationToken, TaskPoolScheduler.Default);
        }

        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }
    }

    public static class ChannelExtensions
    {
        public static ChannelReader<T> ConnectToChannel<T>(this IObservable<T> observable,
            CancellationToken cancellationToken = default, IScheduler scheduler = null)
        {
            var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions()
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true
            });

            var disposable = observable
                .ObserveOn(scheduler ?? TaskPoolScheduler.Default)
                .Select(z => Observable.FromAsync(ct =>
                    channel.Writer.WriteAsync(z, ct).AsTask()))
                .Concat()
                .Subscribe(_ => { },
                    e => channel.Writer.Complete(e),
                    () => channel.Writer.Complete()
                );

            cancellationToken.Register(disposable.Dispose);
            return channel.Reader;
        }
    }
}