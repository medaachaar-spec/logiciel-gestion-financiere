using System.Reflection;
using System.Data.Common;
using System.Text.Json;
using System.IO;
using FinancialManagement.Desktop.ViewModels;
using FinancialManagement.Desktop.Services;
using Microsoft.Data.Sqlite;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Application.CustomerAdvances;
using FinancialManagement.Application.SupplierAdvances;
using FinancialManagement.Application.Payments;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

static class Probe
{
    static readonly DateOnly Day = new(2026,9,2);
    static void Print(string name, object value) => Console.WriteLine(name+": "+JsonSerializer.Serialize(value));
    public static async Task Main(string[] args)
    {
        if(args.Contains("extra")){await Extra();return;}
        if(args.Contains("workflow")){await Workflow();return;}
        foreach(var supplier in new[]{false,true})
        {
            var side=supplier?"Supplier":"Customer";
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(1000); var payment=f.Payment(1200); f.Allocate(payment,target,1200);
                await f.Login();
                Print(side+" overallocation unfiltered",await f.Page());
                foreach(var status in Enum.GetValues<PaymentObligationStatus>()) Print(side+" overallocation "+status,await f.Page(status));
                Print(side+" overallocation detail",await f.Detail(target));
            }
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(long.MaxValue); f.Allocate(f.Payment(long.MaxValue),target,long.MaxValue);f.Allocate(f.Payment(1),target,1);await f.Login();
                try{ Print(side+" overflow",await f.Page()); }catch(Exception ex){Print(side+" overflow exception",ex.GetBaseException().Message);}
            }
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(1000);var payment=f.Payment(1000,wrongParty:true);f.Allocate(payment,target,1000);await f.Login();
                Print(side+" mismatched party page",await f.Page());Print(side+" mismatched party detail",await f.Detail(target));
            }
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(1000);var payment=f.Payment(1000);f.Allocate(payment,target,400);f.Allocate(payment,target,400);await f.Login();
                Print(side+" repeated allocation rows",await f.Detail(target));
            }
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(1000);var payment=f.Payment(1000);f.Allocate(payment,target,700);await f.Login();
                Print(side+" 1000/700 before advance",await f.Detail(target));
                object applied=supplier
                    ? await f.Services.GetRequiredService<ISupplierAdvanceService>().ApplyAsync(new(payment,[new SupplierAdvanceAllocationInput(target,null,300)]))
                    : await f.Services.GetRequiredService<ICustomerAdvanceService>().ApplyAsync(new(payment,[new CustomerAdvanceAllocationInput(target,null,300)]));
                Print(side+" explicit advance apply",applied);Print(side+" after advance",await f.Detail(target));
            }
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(1000);var payment=f.Payment(1000);await f.Login();
                var hook=new Hook();using var read=f.ReadContext(hook);var service=f.ReadService(read);
                hook.Before=number=>{if(number==3)f.Allocate(payment,target,1000);};
                Print(side+" concurrent payment before revalidation",await f.Page(PaymentObligationStatus.Outstanding,service));
                Print(side+" page commands",hook.Sql.Count);
                File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,side+"-page-sql.txt"),hook.Sql);
                hook.Reset();Print(side+" detail after race",await f.Detail(target,service));Print(side+" detail commands",hook.Sql.Count);
                hook.Reset();hook.Before=number=>{if(number==4){var other=f.Target(1000); f.Allocate(f.Payment(1000),other,1000);}};
                // This independent unrelated commit must not affect the detail.
                Print(side+" unrelated commit detail",await f.Detail(target,service));
            }
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(1000);var payment=f.Payment(1000);await f.Login();
                var hook=new Hook();using var read=f.ReadContext(hook);var service=f.ReadService(read);
                hook.Before=number=>{if(number==4)f.Allocate(payment,target,500);};
                Print(side+" concurrent detail history change",await f.Detail(target,service));
            }
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(1000);var payment=f.Payment(1000);f.Allocate(payment,target,500);await f.Login();
                f.Db.Database.ExecuteSqlRaw($"UPDATE {side}Payments SET PaymentMethod = '999' WHERE Id = {payment}");
                Print(side+" invalid method list",await f.Page());Print(side+" invalid method detail",await f.Detail(target));
            }
        }
    }
    static async Task Workflow()
    {
        foreach(var supplier in new[]{false,true})
        {
            var side=supplier?"Supplier":"Customer";
            using(var f=new Fixture(supplier))
            {
                f.Target(1000);await f.Login();using var coordinator=new DashboardOperationCoordinator();var vm=f.ViewModel(coordinator);await vm.LoadAsync();
                vm.ShowCreateForObligationCommand.Execute(vm.Payments[0]);
                Print(side+" quick lock",new{vm.IsTargetObligationLocked,vm.CanChangeCreateTarget,Obligations=vm.Obligations.Count,ClearEnabled=vm.ClearSelectedPartyCommand.CanExecute(null)});
                vm.PaymentAmount="5,00";vm.Obligations[0].AllocationAmount="5,00";vm.PrepareCreateCommand.Execute(null);
                var cmd=(AsyncRelayCommand)vm.ConfirmCreateCommand;await Task.WhenAll(cmd.ExecuteAsync(null),cmd.ExecuteAsync(null));
                Print(side+" after double confirmation",new{Payments=supplier?f.Db.SupplierPayments.Count():f.Db.CustomerPayments.Count(),vm.SuccessMessage,vm.ListErrorMessage,vm.IsDetailVisible,vm.DetailPaidAmount,vm.DetailRemainingAmount,History=vm.DetailHistory.Count,RowPaid=vm.Payments[0].PaidAmount,RowRemaining=vm.Payments[0].RemainingAmount});vm.Stop();
            }
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(1000);await f.Login();using var coordinator=new DashboardOperationCoordinator();var vm=f.ViewModel(coordinator);await vm.LoadAsync();vm.ShowCreateForObligationCommand.Execute(vm.Payments[0]);
                f.Allocate(f.Payment(1000),target,1000);vm.PrepareCreateCommand.Execute(null);await ((AsyncRelayCommand)vm.ConfirmCreateCommand).ExecuteAsync(null);
                Print(side+" stale target rejected",new{Payments=supplier?f.Db.SupplierPayments.Count():f.Db.CustomerPayments.Count(),vm.SuccessMessage,vm.WorkflowErrorMessage,vm.IsCreateVisible,vm.IsDetailVisible});vm.Stop();
            }
        }
    }
    static async Task Extra()
    {
        foreach(var supplier in new[]{false,true})
        {
            var side=supplier?"Supplier":"Customer";
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(long.MaxValue); f.Allocate(f.Payment(long.MaxValue),target,long.MaxValue);await f.Login();
                Print(side+" exact long max page",await f.Page(PaymentObligationStatus.Settled));Print(side+" exact long max detail",await f.Detail(target));
            }
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(1000);var other=f.Target(1000);var payment=f.Payment(600);f.Allocate(payment,target,400);f.Allocate(payment,other,400);await f.Login();
                Print(side+" contributing payment overallocated",await f.Page(PaymentObligationStatus.PartiallyPaid));
                using var coordinator=new DashboardOperationCoordinator();var vm=f.ViewModel(coordinator);await vm.LoadAsync();
                Print(side+" initial failed read UI",new{vm.ShowEmptyState,vm.ListErrorMessage,vm.TotalCount,vm.PageNumberDescription});
                await vm.RefreshAsync();Print(side+" manual refresh failed read UI",new{vm.ShowEmptyState,vm.ListErrorMessage});vm.Stop();
            }
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(1000);var payment=f.Payment(1000);await f.Login();
                var hook=new Hook();using var db=f.ReadContext(hook);var svc=f.ReadService(db);hook.Before=n=>{if(n==2)f.Allocate(payment,target,1000);};
                Print(side+" race between count and page",await f.Page(PaymentObligationStatus.Outstanding,svc));
            }
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(1000);var payment=f.Payment(1000);f.Allocate(payment,target,700);await f.Login();
                using var coordinator=new DashboardOperationCoordinator();var vm=f.ViewModel(coordinator);await vm.LoadAsync();
                await ((AsyncRelayCommand)vm.OpenDetailCommand).ExecuteAsync(vm.Payments[0]);
                Print(side+" advance UI after opening obligation",new{vm.CanApplyAdvancePermission,vm.CanApplySelectedAdvance,CommandEnabled=vm.ShowAdvanceCommand.CanExecute(null)});vm.Stop();
            }
            foreach(var corrupt in new[]{"zeroOriginal","negativeAllocation","orphanPayment","orphanParty"})
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(1000);var payment=f.Payment(1000);f.Allocate(payment,target,500);await f.Login();f.Db.Database.OpenConnection();
                f.Db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF");f.Db.Database.ExecuteSqlRaw("PRAGMA ignore_check_constraints = ON");
                var sql=corrupt switch{
                    "zeroOriginal"=>$"UPDATE {(supplier?"Purchases":"Sales")} SET TotalAmountMinor = 0",
                    "negativeAllocation"=>$"UPDATE {side}PaymentAllocations SET AmountMinor = -500",
                    "orphanPayment"=>$"DELETE FROM {side}Payments",
                    _=>$"DELETE FROM {(supplier?"Suppliers":"Clients")}"};
                f.Db.Database.ExecuteSqlRaw(sql);
                Print(side+" "+corrupt+" page",await f.Page());Print(side+" "+corrupt+" detail",await f.Detail(target));
            }
            using(var f=new Fixture(supplier))
            {
                var target=f.Target(1000);f.Allocate(f.Payment(1000),target,500);await f.Login();
                var hook=new Hook{CapturePlans=true};using var db=f.ReadContext(hook);var svc=f.ReadService(db);
                await f.Page(PaymentObligationStatus.PartiallyPaid,svc);
                File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,side+"-query-plans.txt"),hook.Plans);
                Print(side+" captured query plans",hook.Plans);
            }
        }
    }
    sealed class Fixture:IDisposable
    {
        readonly object obj;readonly Type type;readonly bool supplier;readonly User user; readonly Client client;readonly Supplier vendor;int counter;
        public FinancialManagementDbContext Db{get;}
        public IServiceProvider Services{get;}
        public Fixture(bool isSupplier)
        {
            supplier=isSupplier;type=typeof(FinancialManagement.Tests.PaymentObligationReadServiceTests).Assembly.GetType("FinancialManagement.Tests.PaymentReadTestDatabase")!;
            obj=Activator.CreateInstance(type)!;Db=(FinancialManagementDbContext)type.GetProperty("Context")!.GetValue(obj)!;
            Services=((IServiceScope)type.GetField("scope",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(obj)!).ServiceProvider;
            user=(User)Call("AddUser","probe",UserRole.Responsable)!;client=(Client)Call("AddClient","Party A")!;vendor=(Supplier)Call("AddSupplier","Party A")!;
        }
        object? Call(string name, params object?[] supplied)
        {
            var method=type.GetMethod(name)!;var args=method.GetParameters().Select((p,i)=>i<supplied.Length?supplied[i]:p.DefaultValue).ToArray();return method.Invoke(obj,args);
        }
        public Task Login()=>(Task)Call("AuthenticateAsync",user)!;
        public long Target(long amount)=>supplier?((Purchase)Call("AddPurchase",user,vendor,"TARGET-"+(++counter),amount)!).Id:((Sale)Call("AddSale",user,"TARGET-"+(++counter),amount,client.Id)!).Id;
        public long Payment(long amount,bool wrongParty=false)
        {
            var party=supplier?vendor.Id:client.Id;
            if(wrongParty)party=supplier?((Supplier)Call("AddSupplier","Party B")!).Id:((Client)Call("AddClient","Party B")!).Id;
            return supplier?((SupplierPayment)Call("AddSupplierPayment",user,"PAY-"+(++counter),Day,amount,party)!).Id:((CustomerPayment)Call("AddCustomerPayment",user,"PAY-"+(++counter),Day,amount,party)!).Id;
        }
        public void Allocate(long payment,long target,long amount)
        {
            if(supplier)Call("AddSupplierAllocation",Db.SupplierPayments.Find(payment),amount,target);
            else Call("AddCustomerAllocation",Db.CustomerPayments.Find(payment),amount,target);
        }
        public async Task<object> Page(PaymentObligationStatus? status=null,object? service=null)=>supplier
            ? await ((ISupplierPaymentObligationReadService)(service??Services.GetRequiredService<ISupplierPaymentObligationReadService>())).GetPageAsync(new(Status:status))
            : await ((ICustomerPaymentObligationReadService)(service??Services.GetRequiredService<ICustomerPaymentObligationReadService>())).GetPageAsync(new(Status:status));
        public async Task<object> Detail(long id,object? service=null)=>supplier
            ? await ((ISupplierPaymentObligationReadService)(service??Services.GetRequiredService<ISupplierPaymentObligationReadService>())).GetDetailAsync(SupplierPaymentObligationTargetType.Purchase,id)
            : await ((ICustomerPaymentObligationReadService)(service??Services.GetRequiredService<ICustomerPaymentObligationReadService>())).GetDetailAsync(CustomerPaymentObligationTargetType.Sale,id);
        public FinancialManagementDbContext ReadContext(Hook hook)=>new(new DbContextOptionsBuilder<FinancialManagementDbContext>().UseSqlite(Db.Database.GetConnectionString()).AddInterceptors(hook).Options);
        public object ReadService(FinancialManagementDbContext db)=>Activator.CreateInstance(typeof(FinancialManagementDbContext).Assembly.GetType($"FinancialManagement.Infrastructure.{(supplier?"Supplier":"Customer")}Payments.{(supplier?"Supplier":"Customer")}PaymentObligationReadService")!,db,Services.GetRequiredService<ICurrentUserSession>(),Services.GetRequiredService<IAuthorizationService>())!;
        public PaymentManagementViewModelBase ViewModel(DashboardOperationCoordinator coordinator)=>(PaymentManagementViewModelBase)Activator.CreateInstance(supplier?typeof(SupplierPaymentsViewModel):typeof(CustomerPaymentsViewModel),(supplier?typeof(SupplierPaymentsViewModel):typeof(CustomerPaymentsViewModel)).GetConstructors().Single().GetParameters().Select(p=>p.ParameterType==typeof(DashboardOperationCoordinator)?(object)coordinator:Services.GetRequiredService(p.ParameterType)).ToArray())!;
        public void Dispose()=>((IDisposable)obj).Dispose();
    }
    sealed class Hook:DbCommandInterceptor
    {
        public List<string> Sql{get;}=[];public Action<int>? Before;
        public bool CapturePlans;public List<string> Plans{get;}=[];
        public void Reset(){Sql.Clear();Before=null;}
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default)
        {
            Sql.Add(command.CommandText);Before?.Invoke(Sql.Count);
            if(CapturePlans)
            {
                using var conn=new SqliteConnection(command.Connection!.ConnectionString);conn.Open();using var plan=conn.CreateCommand();plan.CommandText="EXPLAIN QUERY PLAN "+command.CommandText;
                foreach(DbParameter p in command.Parameters)plan.Parameters.AddWithValue(p.ParameterName,p.Value??DBNull.Value);
                using var rows=plan.ExecuteReader();Plans.Add("Query "+Sql.Count);while(rows.Read())Plans.Add(rows.GetString(3));
            }
            return ValueTask.FromResult(result);
        }
    }
}
