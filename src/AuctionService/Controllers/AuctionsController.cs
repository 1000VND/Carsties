using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Contracts;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Controllers
{
    [ApiController]
    [Route("api/auctions")]
    public class AuctionsController : ControllerBase
    {
        private readonly AuctionDbContext _context;
        private readonly IMapper _mapper;
        private readonly IPublishEndpoint _publishEndpoint;

        public AuctionsController
            (
                AuctionDbContext context,
                IMapper mapper,
                IPublishEndpoint publishEndpoint
            )
        {
            _context = context;
            _mapper = mapper;
            _publishEndpoint = publishEndpoint;
        }

        /// <summary>Lấy tất cả dữ liệu đấu giá</summary>
        /// <param name="date">The date.</param>
        /// Author: hunglq
        /// Created: 5/9/2025
        /// Modified: 5/9/2025 - hunglq - description
        [HttpGet]
        public async Task<ActionResult<List<AuctionDto>>> GetAllAuctions(string date)
        {
            // Tạo truy vấn lấy tất cả các bản ghi Auction, sắp xếp theo tên hãng xe (Make)
            var query = _context.Auctions.OrderBy(x => x.Item.Make).AsQueryable();

            // Nếu có truyền tham số date, chỉ lấy các bản ghi có UpdatedAt lớn hơn thời điểm này (lọc theo ngày cập nhật)
            if (!string.IsNullOrEmpty(date))
            {
                query = query.Where(x => x.UpdatedAt.CompareTo(DateTime.Parse(date).ToUniversalTime()) > 0);
            }

            // Dùng AutoMapper để chuyển đổi kết quả sang danh sách AuctionDto và trả về cho client
            return await query.ProjectTo<AuctionDto>(_mapper.ConfigurationProvider).ToListAsync();
        }

        /// <summary>Lấy xe đấu giá theo Id</summary>
        /// <param name="id">Id của xe</param>
        /// Author: hunglq
        /// Created: 5/9/2025
        /// Modified: 5/9/2025 - hunglq - description
        [HttpGet("{id}")]
        public async Task<ActionResult<AuctionDto>> GetAuctionById(Guid id)
        {
            // Tìm bản ghi Auction theo Id, bao gồm cả thông tin Item liên quan
            var auction = await _context.Auctions
                .Include(x => x.Item)
                .FirstOrDefaultAsync(x => x.Id == id);

            // Nếu không tìm thấy auction, trả về NotFound (404)
            if (auction == null) return NotFound();

            // Dùng AutoMapper để chuyển đổi entity Auction sang AuctionDto và trả về cho client
            return _mapper.Map<AuctionDto>(auction);
        }

        [HttpPost]
        public async Task<ActionResult<AuctionDto>> CreateAuction(CreateAuctionDto auctionDto)
        {
            // Ánh xạ dữ liệu từ CreateAuctionDto sang entity Auction
            var auction = _mapper.Map<Auction>(auctionDto);

            // Gán giá trị seller tạm thời (có thể thay bằng thông tin người dùng thực tế)
            auction.Seller = "test";

            // Thêm bản ghi auction mới vào DbContext
            _context.Auctions.Add(auction);

            // Ánh xạ lại sang AuctionDto để trả về cho client
            var newAuction = _mapper.Map<AuctionDto>(auction);

            // Gửi sự kiện AuctionCreated qua message bus (MassTransit)
            await _publishEndpoint.Publish(_mapper.Map<AuctionCreated>(newAuction));

            // Lưu thay đổi vào database
            var result = await _context.SaveChangesAsync() > 0;

            // Nếu lưu thất bại, trả về lỗi
            if (!result) return BadRequest("Could not save changes to the DB");

            // Trả về kết quả thành công, kèm theo thông tin auction vừa tạo
            return CreatedAtAction(nameof(GetAuctionById),
                new { auction.Id }, newAuction);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult> UpdateAuction(Guid id, UpdateAuctionDto updateAuctionDto)
        {
            var auction = await _context.Auctions.Include(x => x.Item)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (auction == null) return NotFound();

            auction.Item.Make = updateAuctionDto.Make ?? auction.Item.Make;
            auction.Item.Model = updateAuctionDto.Model ?? auction.Item.Model;
            auction.Item.Color = updateAuctionDto.Color ?? auction.Item.Color;
            auction.Item.Mileage = updateAuctionDto.Mileage ?? auction.Item.Mileage;
            auction.Item.Year = updateAuctionDto.Year ?? auction.Item.Year;

            await _publishEndpoint.Publish(_mapper.Map<AuctionUpdated>(auction));

            var result = await _context.SaveChangesAsync() > 0;

            if (result) return Ok();

            return BadRequest("Problem saving changes");
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteAuction(Guid id)
        {
            var auction = await _context.Auctions.FindAsync(id);

            if (auction == null) return NotFound();

            _context.Auctions.Remove(auction);

            await _publishEndpoint.Publish<AuctionDeleted>(new { Id = auction.Id.ToString() });

            var result = await _context.SaveChangesAsync() > 0;

            if (!result) return BadRequest("Could not update DB");

            return Ok();
        }
    }
}
